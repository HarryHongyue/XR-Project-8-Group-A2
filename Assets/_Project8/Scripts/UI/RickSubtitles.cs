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
    /// Long messages are paginated (max 2 lines per page) using language-aware
    /// boundaries - CJK text splits on characters, Latin text on words. Pages
    /// advance on UNSCALED time so narration stays readable while the
    /// simulation is paused.
    ///
    /// Semantics: a new ShowNarration replaces any pending story pages; a
    /// ShowAnswer temporarily interrupts the story queue and the latest story
    /// position resumes once the answer finishes.
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
        const float MinDwell = 3f, MaxDwell = 10f;
        const float DwellPerChar = 0.055f;

        readonly List<string> _story = new List<string>();
        int _storyIndex;
        readonly List<string> _answer = new List<string>();
        int _answerIndex;
        bool _showingAnswer;
        string _pendingVoice;   // latest narration utterance waiting on an answer
        float _pageStart = -1f;
        Font _baseFont;
        bool _cjk;

        /// <summary>Story narration event: replaces any PENDING story pages but
        /// never cuts off an AI answer that is currently being read - the latest
        /// story page simply resumes once the answer finishes.</summary>
        public void ShowNarration(string narration)
        {
            _cjk = ExperienceSettings.IsChinese;
            ApplyFont();
            PaginateInto(narration, _story);
            _storyIndex = 0;
            // Voice: speak the whole utterance once - either now, or queued
            // behind the AI answer that is currently being read aloud.
            if (voice != null)
            {
                if (_showingAnswer) _pendingVoice = narration;
                else voice.Speak(narration);
            }
            if (!_showingAnswer) _pageStart = -1f; // keep the answer's own clock
            RenderCurrent();
        }

        /// <summary>AI answer: temporarily interrupts the story queue. The
        /// story resumes from its latest page once the answer has been read.
        /// Chooses the CJK font itself - an answer may be the first text shown.
        /// `voiceText` is the utterance spoken once (null = silent, e.g. the
        /// "thinking" placeholder); it may differ from the displayed text so a
        /// "Rick: " prefix never reaches speech.</summary>
        public void ShowAnswer(string display, string voiceText = null)
        {
            _cjk = ExperienceSettings.IsChinese;
            ApplyFont();
            PaginateInto(display, _answer);
            _answerIndex = 0;
            if (_answer.Count > 0) _showingAnswer = true;
            _pageStart = -1f;
            RenderCurrent();
            if (voice != null && !string.IsNullOrEmpty(voiceText))
                voice.Speak(voiceText);
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
        public int PendingStoryPages => _story.Count - _storyIndex;
        public bool IsShowingAnswer => _showingAnswer;

        void Update()
        {
            var pages = _showingAnswer ? _answer : _story;
            int idx = _showingAnswer ? _answerIndex : _storyIndex;
            if (idx >= pages.Count || _pageStart < 0f) return;

            float dwell = Mathf.Clamp(pages[idx].Length * DwellPerChar, MinDwell, MaxDwell);
            if (Time.unscaledTime - _pageStart >= dwell) AdvancePage();
        }

        /// <summary>Single page-advance transition shared by Update() and the
        /// test hook; resumes the story queue (and any narration voice held
        /// back during an AI answer) when the answer finishes.</summary>
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
                return;
            }
            if (_showingAnswer)
            {
                // Answer finished -> hand back to the latest story position and
                // release the narration utterance queued behind it.
                _showingAnswer = false;
                _pageStart = -1f;
                if (_pendingVoice != null && voice != null)
                    voice.Speak(_pendingVoice);
                _pendingVoice = null;
                RenderCurrent();
                return;
            }
            // Story queue exhausted: keep the last page up a beat, then clear.
            text.text = "";
            _pageStart = -1f;
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

        /// <summary>Measured pagination: every returned page renders in at most
        /// MaxLinesPerPage lines inside `width` pixels at the given font - the
        /// real TextGenerator does the measuring, so Latin words, CJK strings,
        /// explicit newlines and unbroken overlong tokens are all handled with
        /// no character ever dropped. Static and side-effect free so editor
        /// regression can exercise it without a Canvas.</summary>
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

            foreach (string paragraph in raw.Replace("\r", "").Split('\n'))
            {
                // Tokenize: Latin splits on words (joined with spaces); CJK and
                // overlong words fall back to characters (joined seamlessly).
                var units = new List<KeyValuePair<string, bool>>(); // text, spaceBefore
                if (IsMostlyCjk(paragraph))
                    foreach (char c in paragraph.Trim())
                    {
                        if (!char.IsWhiteSpace(c))
                            units.Add(new KeyValuePair<string, bool>(c.ToString(), false));
                    }
                else
                    foreach (string w in paragraph.Trim().Split(' '))
                        if (w.Length > 0)
                            units.Add(new KeyValuePair<string, bool>(w, true));

                var page = new StringBuilder();
                foreach (var u in units)
                {
                    string candidate = page.Length == 0 ? u.Key
                        : page + (u.Value ? " " : "") + u.Key;
                    if (Fits(candidate)) { page.Clear(); page.Append(candidate); continue; }
                    if (page.Length > 0)
                    {
                        pages.Add(page.ToString());
                        page.Clear();
                        if (Fits(u.Key)) { page.Append(u.Key); continue; }
                    }
                    // A single unit wider than the box: break it character by
                    // character - nothing is ever truncated away.
                    foreach (char c in u.Key)
                    {
                        if (Fits(page.ToString() + c)) page.Append(c);
                        else
                        {
                            if (page.Length > 0) pages.Add(page.ToString());
                            page.Clear();
                            page.Append(c);
                        }
                    }
                }
                if (page.Length > 0) pages.Add(page.ToString());
            }
            return pages;
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

        /// <summary>Char-budget fallback used only when no font is wired yet.</summary>
        static void FallbackPaginate(string raw, List<string> pages)
        {
            int budget = IsMostlyCjk(raw) ? MaxCjkCharsPerPage : MaxLatinCharsPerPage;
            string s = raw.Trim();
            for (int i = 0; i < s.Length; i += budget)
                pages.Add(s.Substring(i, Mathf.Min(budget, s.Length - i)));
        }
    }
}
