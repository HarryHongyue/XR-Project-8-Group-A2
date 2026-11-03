using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Project8.UI
{
    /// <summary>
    /// One shared subtitle presenter for story narration AND Rick's AI answers.
    /// The canvas it lives on is intentionally non-interactive: no background
    /// Image, no buttons, no GraphicRaycaster - just a white two-line Text with
    /// a black shadow, always visible at the lower centre of the canopy.
    ///
    /// Long messages are paginated by COMPLETE SENTENCES: English, Chinese and
    /// Dutch text splits on its own sentence terminators first, so every page
    /// is a whole spoken unit and TTS never reads half a sentence. Only an
    /// overlong single sentence falls back to clause/word packing (still max
    /// 2 rendered lines, no character ever dropped). Pages advance on UNSCALED
    /// time so narration stays readable while the simulation is paused.
    ///
    /// Narrations and AI answers are serialized: a new line never replaces
    /// speech already on air. On Android, RickVoice polls the platform TTS
    /// status so subtitle pages wait for the spoken utterance too.
    /// </summary>
    public class RickSubtitles : MonoBehaviour
    {
        [Tooltip("The subtitle Text - raycastTarget off, font 28, white + black shadow.")]
        public Text text;
        [Tooltip("Optional voice: narration is spoken by the presenter so an " +
            "in-flight AI answer is never cut off - a narration arriving during " +
            "an answer speaks once the answer finishes.")]
        public RickVoice voice;

        const int MaxLinesPerPage = 2;
        const int MaxLineChars = 80;      // Latin wrap width
        const int MaxCjkCharsPerPage = 40;
        const int MaxLatinCharsPerPage = 110;
        const float MinDwell = 3f, MaxDwell = 14f;
        const float DwellPerChar = 0.085f;

        readonly List<string> _story = new List<string>();
        int _storyIndex;
        readonly List<string> _answer = new List<string>();
        int _answerIndex;
        readonly Queue<string> _pendingStory = new Queue<string>();
        readonly Queue<QueuedAnswer> _pendingAnswers = new Queue<QueuedAnswer>();
        bool _showingAnswer;
        /// <summary>Answers displayed with no voiceText (the "thinking"
        /// placeholder, system hints) render silently - only pages carrying a
        /// real utterance are spoken.</summary>
        bool _answerHasVoice;
        float _pageStart = -1f;
        Font _baseFont;
        bool _cjk;

        struct QueuedAnswer
        {
            public string display;
            public string voiceText;

            public QueuedAnswer(string display, string voiceText)
            {
                this.display = display;
                this.voiceText = voiceText;
            }
        }

        /// <summary>Queue a story line behind any narration or AI answer already
        /// being shown/spoken. A later story event can never cut the current clip.</summary>
        public void ShowNarration(string narration)
        {
            if (string.IsNullOrWhiteSpace(narration)) return;
            _cjk = ExperienceSettings.IsChinese;
            ApplyFont();
            MissionLogUI.Add("Rick", narration);
            if (HasActiveUtterance() || _pendingStory.Count > 0 ||
                _pendingAnswers.Count > 0)
            {
                _pendingStory.Enqueue(narration);
                return;
            }
            BeginNarration(narration);
        }

        void BeginNarration(string narration)
        {
            _cjk = ExperienceSettings.IsChinese;
            ApplyFont();
            _showingAnswer = false;
            _answer.Clear();
            _answerIndex = 0;
            PaginateInto(narration, _story);
            _storyIndex = 0;
            _pageStart = -1f;
            RenderCurrent();
        }

        /// <summary>Queue an AI answer without interrupting an active utterance.
        /// Chooses the CJK font itself - an answer may be the first text shown.
        /// `voiceText` is the utterance spoken once (null = silent, e.g. the
        /// "thinking" placeholder); it may differ from the displayed text so a
        /// "Rick: " prefix never reaches speech.</summary>
        public void ShowAnswer(string display, string voiceText = null)
        {
            _cjk = ExperienceSettings.IsChinese;
            ApplyFont();
            // The transient "thinking" placeholder is not a real answer -
            // keep it out of the log.
            if (display.IndexOf("thinking", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                !display.Contains("思考中") && !display.Contains("denken"))
                MissionLogUI.Add("Rick", display);
            if (HasActiveUtterance() || _pendingStory.Count > 0 ||
                _pendingAnswers.Count > 0)
            {
                _pendingAnswers.Enqueue(new QueuedAnswer(display, voiceText));
                return;
            }
            BeginAnswer(display, voiceText);
        }

        void BeginAnswer(string display, string voiceText)
        {
            _cjk = ExperienceSettings.IsChinese;
            ApplyFont();
            PaginateInto(display, _answer);
            _answerIndex = 0;
            if (_answer.Count > 0) _showingAnswer = true;
            _answerHasVoice = !string.IsNullOrEmpty(voiceText);
            _pageStart = -1f;
            RenderCurrent();
        }

        void ApplyFont()
        {
            if (text == null) return;
            if (_baseFont == null) _baseFont = text.font;
            if (_cjk) LocalizedText.UseChineseFont(text);
            else if (_baseFont != null) text.font = _baseFont;
        }

        /// <summary>Deterministic test/editor hook: jump straight to the next
        /// page (or finish the current queue) - the same transition Update()
        /// performs once a page's dwell elapses.</summary>
        public void AdvancePageForTest() => AdvancePage();

        /// <summary>Pages remaining or currently showing (tests/status).</summary>
        public int PendingStoryPages => _story.Count - _storyIndex + _pendingStory.Count;
        public bool IsShowingAnswer => _showingAnswer;
        /// <summary>True while any narration/answer is still on screen - used to
        /// fire "after the explanation finishes" cues like button spotlights.</summary>
        public bool Busy =>
            HasActiveUtterance() || _pendingStory.Count > 0 || _pendingAnswers.Count > 0;

        bool HasActiveUtterance()
        {
            var pages = _showingAnswer ? _answer : _story;
            int index = _showingAnswer ? _answerIndex : _storyIndex;
            return index < pages.Count || (voice != null && voice.IsSpeaking);
        }

        void Update()
        {
            var pages = _showingAnswer ? _answer : _story;
            int idx = _showingAnswer ? _answerIndex : _storyIndex;
            if (idx >= pages.Count)
            {
                if ((voice == null || !voice.IsSpeaking) &&
                    (_pendingStory.Count > 0 || _pendingAnswers.Count > 0))
                    RenderCurrent();
                return;
            }
            if (_pageStart < 0f) return;
            // Hold the page while its line is still being spoken.
            if (voice != null && voice.IsSpeaking) return;

            float dwell = Mathf.Clamp(pages[idx].Length * DwellPerChar, MinDwell, MaxDwell);
            if (Time.unscaledTime - _pageStart >= dwell) AdvancePage();
        }

        /// <summary>Single page-advance transition shared by Update() and the
        /// test hook. Queued utterances start only after the current clip ends.</summary>
        void AdvancePage()
        {
            if (_showingAnswer) _answerIndex++; else _storyIndex++;
            _pageStart = Time.unscaledTime;
            RenderCurrent();
        }

        void RenderCurrent()
        {
            if (text == null) return;
            var pages = _showingAnswer ? _answer : _story;
            int idx = _showingAnswer ? _answerIndex : _storyIndex;
            if (idx < pages.Count)
            {
                if (_pageStart < 0f) _pageStart = Time.unscaledTime;
                text.text = pages[idx];
                // Speak the visible page; Update waits for this clip before
                // advancing, so the next subtitle cannot overtake the audio.
                if (voice != null && (!_showingAnswer || _answerHasVoice))
                    voice.Speak(pages[idx]);
                return;
            }
            if (_showingAnswer)
            {
                _showingAnswer = false;
            }

            _answer.Clear();
            _answerIndex = 0;
            _answerHasVoice = false;
            _pageStart = -1f;
            if (_pendingAnswers.Count > 0)
            {
                var next = _pendingAnswers.Dequeue();
                BeginAnswer(next.display, next.voiceText);
                return;
            }
            if (_pendingStory.Count > 0)
            {
                BeginNarration(_pendingStory.Dequeue());
                return;
            }

            text.text = "";
        }

        // ---------------------------------------------------------- pagination
        void PaginateInto(string raw, List<string> pages)
        {
            pages.Clear();
            Font f = text != null ? text.font : null;
            float w = text != null
                ? Mathf.Max(50f, text.rectTransform.rect.width - 24f) : 800f;
            int fs = text != null ? text.fontSize : 28;
            if (f != null)
                pages.AddRange(PaginateForDisplay(raw, w, f, fs));
            else
                FallbackPaginate(raw, pages); // no font wired (should not happen)
        }

        /// <summary>Measured, sentence-aware pagination. Each complete sentence -
        /// English/Dutch ending in '.', '!' or '?', Chinese in '。！？；' -
        /// becomes its own page whenever it fits inside MaxLinesPerPage lines,
        /// so the voice never reads half a sentence on one page and the rest on
        /// the next. A single sentence too long for one page is split at clause
        /// boundaries (commas, semicolons, pauses) first and only then falls
        /// back to the greedy word/character packing - every character is kept.
        /// Static and side-effect free so editor regression can exercise it
        /// without a Canvas.</summary>
        public static List<string> PaginateForDisplay(string raw, float width,
            Font font, int fontSize = 28)
        {
            var pages = new List<string>();
            if (string.IsNullOrWhiteSpace(raw) || font == null || width <= 0f)
                return pages;
            var gen = new TextGenerator();
            var settings = new TextGenerationSettings
            {
                font = font,
                fontSize = fontSize,
                lineSpacing = 1f,
                richText = false,
                generationExtents = new Vector2(width, 2000f),
                horizontalOverflow = HorizontalWrapMode.Wrap,
                verticalOverflow = VerticalWrapMode.Overflow,
                generateOutOfBounds = true,
                textAnchor = TextAnchor.UpperLeft,
                scaleFactor = 1f,
            };
            bool Fits(string candidate)
            {
                gen.Populate(candidate, settings);
                return gen.lineCount <= MaxLinesPerPage;
            }

            var spill = new StringBuilder();
            void Flush()
            {
                if (spill.Length == 0) return;
                string t = spill.ToString().Trim();
                if (t.Length > 0) pages.Add(t);
                spill.Clear();
            }

            foreach (string paragraph in raw.Replace("\r", "").Split('\n'))
            {
                foreach (string sentence in SplitSentences(paragraph))
                {
                    if (Fits(sentence))
                    {
                        // One complete sentence per page - the speech unit.
                        Flush();
                        pages.Add(sentence);
                        continue;
                    }
                    // Overlong sentence: break at clause boundaries before
                    // ever touching word/character packing.
                    foreach (string clause in SplitClauses(sentence))
                    {
                        string candidate = spill.Length == 0 ? clause
                            : spill + " " + clause;
                        if (Fits(candidate))
                        {
                            spill.Clear();
                            spill.Append(candidate);
                            continue;
                        }
                        Flush();
                        if (Fits(clause)) { spill.Append(clause); continue; }
                        PackUnits(clause, Fits, spill, pages);
                    }
                }
            }
            Flush();
            return pages;
        }

        /// <summary>Greedy word/character packing of ONE text (never wider than
        /// the box allows) into `page`, emitting full pages into `pages`.
        /// Latin splits on words, CJK on characters; a token wider than the box
        /// is broken character by character - nothing is truncated.</summary>
        static void PackUnits(string text, System.Func<string, bool> fits,
            StringBuilder page, List<string> pages)
        {
            var units = new List<KeyValuePair<string, bool>>(); // text, spaceBefore
            if (IsMostlyCjk(text))
                foreach (char c in text.Trim())
                {
                    if (!char.IsWhiteSpace(c))
                        units.Add(new KeyValuePair<string, bool>(c.ToString(), false));
                }
            else
                foreach (string w in text.Trim().Split(' '))
                    if (w.Length > 0)
                        units.Add(new KeyValuePair<string, bool>(w, true));

            foreach (var u in units)
            {
                string candidate = page.Length == 0 ? u.Key
                    : page + (u.Value ? " " : "") + u.Key;
                if (fits(candidate)) { page.Clear(); page.Append(candidate); continue; }
                if (page.Length > 0)
                {
                    pages.Add(page.ToString().Trim());
                    page.Clear();
                    if (fits(u.Key)) { page.Append(u.Key); continue; }
                }
                foreach (char c in u.Key)
                {
                    if (fits(page.ToString() + c)) page.Append(c);
                    else
                    {
                        if (page.Length > 0) pages.Add(page.ToString().Trim());
                        page.Clear();
                        page.Append(c);
                    }
                }
            }
        }

        // ------------------------------------------------------ sentence split
        /// <summary>Punctuation that ALWAYS ends a sentence, in every supported
        /// language (Latin '!', '?', ';' and the CJK/full-width variants).</summary>
        static bool IsHardEnd(char c)
        {
            return c == '!' || c == '?' || c == ';' ||
                   c == '！' || c == '？' || c == '；' || c == '…' ||
                   c == '。';
        }

        /// <summary>Closing marks that belong to the sentence they follow.</summary>
        static bool IsCloser(char c)
        {
            return c == '"' || c == '“' || c == '”' || c == '’' || c == '\'' ||
                   c == ')' || c == '）' || c == '」' || c == '』' ||
                   c == '》' || c == '»';
        }

        /// <summary>True when `c` at index `i` in `s` terminates a sentence.
        /// A period counts only when followed by whitespace, a closing mark or
        /// the end of the string - decimals like "0.5" never split.</summary>
        static bool IsSentenceEnd(string s, int i)
        {
            char c = s[i];
            if (IsHardEnd(c) || c == '.')
            {
                if (c != '.') return true;
                if (i + 1 >= s.Length) return true;
                char n = s[i + 1];
                return char.IsWhiteSpace(n) || IsCloser(n);
            }
            return false;
        }

        /// <summary>Splits a paragraph into complete trimmed sentences, keeping
        /// each terminator (and any following closing quotes) attached to its
        /// own sentence. A trailing fragment without punctuation is a sentence
        /// too, so no character is ever lost.</summary>
        static List<string> SplitSentences(string paragraph)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < paragraph.Length; i++)
            {
                sb.Append(paragraph[i]);
                if (!IsSentenceEnd(paragraph, i)) continue;
                // Chained terminators ("?!") and closing quotes belong here.
                while (i + 1 < paragraph.Length &&
                       (IsHardEnd(paragraph[i + 1]) || IsCloser(paragraph[i + 1])))
                    sb.Append(paragraph[++i]);
                string sentence = sb.ToString().Trim();
                sb.Clear();
                if (sentence.Length > 0) result.Add(sentence);
            }
            string tail = sb.ToString().Trim();
            if (tail.Length > 0) result.Add(tail);
            return result;
        }

        /// <summary>Splits an overlong sentence at clause boundaries - commas,
        /// CJK pauses and dashes - keeping the mark attached to its clause.
        /// Colons are NOT boundaries so the "Rick:" speaker tag stays put.</summary>
        static List<string> SplitClauses(string sentence)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < sentence.Length; i++)
            {
                char c = sentence[i];
                sb.Append(c);
                bool clauseEnd = c == ',' || c == '，' || c == '、' ||
                                 c == '，' || c == '—' || c == '–';
                if (!clauseEnd) continue;
                string clause = sb.ToString().Trim();
                sb.Clear();
                if (clause.Length > 0) result.Add(clause);
            }
            string tail = sb.ToString().Trim();
            if (tail.Length > 0) result.Add(tail);
            if (result.Count == 0) result.Add(sentence.Trim());
            return result;
        }

        static bool IsMostlyCjk(string s)
        {
            int total = 0, cjk = 0;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c)) continue;
                total++;
                if (c >= 0x4E00 && c <= 0x9FFF || c >= 0x3000 && c <= 0x303F ||
                    c >= 0xFF00 && c <= 0xFFEF) cjk++;
            }
            return total > 0 && cjk * 2 >= total;
        }

        /// <summary>Char-budget fallback used only when no font is wired yet:
        /// still splits by complete sentences first, then by budget.</summary>
        static void FallbackPaginate(string raw, List<string> pages)
        {
            int budget = IsMostlyCjk(raw) ? MaxCjkCharsPerPage : MaxLatinCharsPerPage;
            foreach (string paragraph in raw.Replace("\r", "").Split('\n'))
            {
                foreach (string sentence in SplitSentences(paragraph))
                {
                    if (sentence.Length <= budget)
                    {
                        pages.Add(sentence);
                        continue;
                    }
                    for (int i = 0; i < sentence.Length; i += budget)
                        pages.Add(sentence.Substring(i,
                            Mathf.Min(budget, sentence.Length - i)));
                }
            }
        }
    }
}
