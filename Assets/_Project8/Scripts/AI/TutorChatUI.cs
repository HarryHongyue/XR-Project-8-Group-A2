using UnityEngine;
using UnityEngine.UI;
using Project8.UI;

namespace Project8.AI
{
    /// <summary>
    /// Rick's chat lives behind a small avatar button: click the avatar to open the
    /// chat window, click again to close. The input row carries a voice button at
    /// its right edge - on Windows it records through UnityEngine.Windows.Speech;
    /// on platforms without speech recognition it shows itself as unavailable.
    /// </summary>
    public class TutorChatUI : MonoBehaviour
    {
        public AITutor tutor;
        public InputField questionInput;
        [Tooltip("Legacy answer widget - unused now that answers go to subtitles.")]
        public Text answerText;
        [Tooltip("Shared subtitle presenter: answers and thinking state appear here.")]
        public RickSubtitles subtitles;
        public RickVoice voice;
        [Header("Avatar + voice")]
        public GameObject chatWindow;
        public Button micButton;
        public Text micLabel;
        [Tooltip("When present, the mic button drives the wake-word/Whisper voice " +
                 "loop instead of the legacy dictation path.")]
        public VoiceInteraction voiceInput;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR
        UnityEngine.Windows.Speech.DictationRecognizer _dictation;
#endif
        bool _listening;
        Image _micIcon;
        static Sprite _micSprite;

        void Start()
        {
            // Repair older saved Main scenes that still have the avatar canvas
            // centred on the cockpit; the current builder puts Rick at lower-right.
            if (transform.parent != null &&
                transform.parent.GetComponent<Project8.Interaction.CockpitControls>() != null)
            {
                // Far-right parking spot, far enough that the control panel
                // (right edge near ship-local x 0.68) can never cover the
                // avatar, the composer below it or the history board above.
                transform.localPosition = new Vector3(1.15f, 1.02f, 1.20f);
                var canvasRect = transform as RectTransform;
                if (canvasRect != null)
                    canvasRect.anchoredPosition = new Vector2(1.15f, 1.02f);
            }
            Camera faceCamera = Camera.main;
            if (faceCamera == null && transform.parent != null)
            {
                var cockpit = transform.parent.GetComponent<Project8.Interaction.CockpitControls>();
                if (cockpit != null) faceCamera = cockpit.seatedCamera;
            }
            if (faceCamera != null)
            {
                Vector3 awayFromEye = transform.position - faceCamera.transform.position;
                if (awayFromEye.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.LookRotation(awayFromEye.normalized, Vector3.up);
            }
            EnsureRickFace();
            if (questionInput != null)
            {
                // Larger, clearer composer text in every scene - older saved
                // scenes still carry the small 15 pt serialized sizes.
                if (questionInput.textComponent != null)
                    questionInput.textComponent.fontSize = 18;
                if (questionInput.placeholder is Text ph0)
                    ph0.fontSize = 18;
                if (ExperienceSettings.IsChinese)
                {
                    if (questionInput.textComponent != null)
                        LocalizedText.UseChineseFont(questionInput.textComponent);
                    var ph = questionInput.placeholder as Text;
                    if (ph != null) { LocalizedText.UseChineseFont(ph); ph.text = "向 Rick 提问……"; }
                }
                else if (ExperienceSettings.IsDutch && questionInput.placeholder is Text phd)
                    phd.text = "Stel Rick een vraag...";
            }
            if (chatWindow == null)
            {
                var child = transform.Find("ChatWindow");
                if (child != null) chatWindow = child.gameObject;
            }
            if (chatWindow != null)
            {
                var rect = chatWindow.GetComponent<RectTransform>();
                if (rect != null) rect.anchoredPosition = new Vector2(-300f, -190f);
                chatWindow.SetActive(false);
            }
            bool speechOk = Application.platform == RuntimePlatform.WindowsEditor ||
                            Application.platform == RuntimePlatform.WindowsPlayer;
            if (micButton != null && !speechOk && voiceInput == null)
                micButton.interactable = false;
            EnsureMicIcon();
            UpdateMicVisual();
        }

        /// <summary>Swap the plain "MIC" caption for a real microphone glyph
        /// drawn at runtime - a capsule head on a cradle arc with a stem and
        /// base - so the control reads as a voice button in every language.</summary>
        void EnsureMicIcon()
        {
            if (micButton == null) return;
            Transform iconT = micButton.transform.Find("MicIcon");
            Image icon;
            if (iconT == null)
            {
                var go = new GameObject("MicIcon",
                    typeof(RectTransform), typeof(Image));
                iconT = go.transform;
                iconT.SetParent(micButton.transform, false);
                var r = (RectTransform)iconT;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(12f, 10f);
                r.offsetMax = new Vector2(-12f, -10f);
                icon = go.GetComponent<Image>();
            }
            else icon = iconT.GetComponent<Image>();
            if (icon == null) return;
            if (_micSprite == null) _micSprite = MakeMicSprite();
            icon.sprite = _micSprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            _micIcon = icon;
            // Kill EVERY caption under the button, not only the wired micLabel -
            // older scenes can carry an unreferenced "MIC" Text that still
            // renders on top of the icon.
            foreach (var t in micButton.GetComponentsInChildren<Text>(true))
            {
                t.text = "";
                t.enabled = false;
                t.raycastTarget = false;
            }
            if (micLabel != null) micLabel.text = "";
        }

        static Sprite MakeMicSprite()
        {
            const int W = 64, H = 64;
            var px = new Color[W * H];
            var cTop = new Vector2(32f, 46f);   // capsule head top-centre
            var cBot = new Vector2(32f, 34f);   // capsule head bottom-centre
            const float rHead = 9f;
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float a = 0f;
                    // capsule head: rounded rectangle
                    float dHead = Mathf.Min((p - cTop).magnitude,
                                          (p - cBot).magnitude);
                    if (p.x >= 23f && p.x <= 41f && p.y >= cBot.y && p.y <= cTop.y)
                        dHead = 0f;
                    a = Mathf.Max(a, Mathf.Clamp01(rHead - dHead + 0.5f));
                    // cradle arc: ring around the head bottom, open at the top
                    float ring = (p - cBot).magnitude;
                    float arc = (p.y < cBot.y - 1f)
                        ? Mathf.Clamp01(1.6f - Mathf.Abs(ring - 15f)) : 0f;
                    a = Mathf.Max(a, arc);
                    // stem
                    if (p.x >= 30.5f && p.x <= 33.5f && p.y >= 6f && p.y <= 16f)
                        a = 1f;
                    // base bar
                    if (p.x >= 22f && p.x <= 42f && p.y >= 2f && p.y <= 5.5f)
                        a = 1f;
                    px[y * W + x] = new Color(1f, 1f, 1f, a);
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, W, H),
                new Vector2(0.5f, 0.5f));
        }

        void EnsureRickFace()
        {
            Transform avatar = transform.Find("Avatar");
            if (avatar == null) return;

            // Enlarge the chip in older serialized scenes too: 120 px chip
            // parked a little higher so the bigger circle stays inside the
            // canvas band (the composer position is repaired in Start()).
            if (avatar is RectTransform avatarRect)
            {
                avatarRect.anchoredPosition = new Vector2(0f, -70f);
                avatarRect.sizeDelta = new Vector2(120f, 120f);
            }

            Transform face = avatar.Find("Face");
            if (face == null)
            {
                var go = new GameObject("Face", typeof(RectTransform), typeof(Image));
                face = go.transform;
                face.SetParent(avatar, false);
                var rect = (RectTransform)face;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(6f, 16f);
                rect.offsetMax = new Vector2(-6f, -4f);
            }

            // Older Main scenes saved "RICK" as a Text Graphic on Face. Two
            // Graphics on one CanvasRenderer fight each other, so retire that
            // legacy Text and put the portrait in its own child Image.
            var legacyText = face.GetComponent<Text>();
            Transform portrait = face;
            var image = face.GetComponent<Image>();
            if (legacyText != null)
            {
                legacyText.text = "";
                legacyText.enabled = false;
                legacyText.raycastTarget = false;
                if (image == null)
                {
                    var portraitGo = new GameObject("Portrait",
                        typeof(RectTransform), typeof(Image));
                    portrait = portraitGo.transform;
                    portrait.SetParent(face, false);
                    var portraitRect = (RectTransform)portrait;
                    portraitRect.anchorMin = Vector2.zero;
                    portraitRect.anchorMax = Vector2.one;
                    portraitRect.offsetMin = new Vector2(4f, 4f);
                    portraitRect.offsetMax = new Vector2(-4f, -4f);
                    image = portraitGo.GetComponent<Image>();
                }
            }
            if (image == null) image = portrait.gameObject.AddComponent<Image>();
            image.enabled = true;
            image.raycastTarget = false;
            if (portrait.GetComponent<RickFaceIcon>() == null)
                portrait.gameObject.AddComponent<RickFaceIcon>();
        }

        /// <summary>Avatar click: open or close the chat window.</summary>
        public void ToggleChat()
        {
            if (chatWindow != null) chatWindow.SetActive(!chatWindow.activeSelf);
        }

        /// <summary>Voice input button at the right edge of the input row -
        /// prefers the Whisper/wake-word loop when one is wired.</summary>
        public void ToggleVoiceInput()
        {
            if (voiceInput != null) { voiceInput.ToggleListening(); return; }
            if (_listening) StopDictation();
            else StartDictation();
        }

        void StartDictation()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            if (_dictation == null)
            {
                _dictation = new UnityEngine.Windows.Speech.DictationRecognizer();
                _dictation.DictationResult += (text, confidence) =>
                {
                    if (questionInput != null)
                        questionInput.text = string.IsNullOrEmpty(questionInput.text)
                            ? text : questionInput.text + " " + text;
                };
                _dictation.DictationComplete += cause =>
                {
                    _listening = false;
                    UpdateMicVisual();
                };
                _dictation.DictationError += (error, hresult) =>
                {
                    _listening = false;
                    UpdateMicVisual();
                };
            }
            try
            {
                _dictation.Start();
                _listening = true;
            }
            catch (System.Exception)
            {
                _listening = false;
            }
            UpdateMicVisual();
#else
            UpdateMicVisual();
#endif
        }

        void StopDictation()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            if (_dictation != null &&
                _dictation.Status == UnityEngine.Windows.Speech.SpeechSystemStatus.Running)
                _dictation.Stop();
#endif
            _listening = false;
            UpdateMicVisual();
        }

        void UpdateMicVisual()
        {
            bool live = _listening ||
                (voiceInput != null && (voiceInput.Listening || voiceInput.Busy));
            if (micLabel != null) { micLabel.text = ""; micLabel.enabled = false; }
            if (_micIcon != null)
                _micIcon.color = live
                    ? new Color(1f, 0.35f, 0.3f, 1f)   // recording red
                    : new Color(0.85f, 0.95f, 1f, 0.95f);
            if (micButton != null)
            {
                var colors = micButton.colors;
                colors.normalColor = live
                    ? new Color(0.9f, 0.35f, 0.35f, 0.9f)
                    : new Color(0.35f, 0.55f, 0.75f, 0.55f);
                micButton.colors = colors;
            }
        }

        /// <summary>An empty focused box only pauses narration for a few
        /// seconds - a stuck focus flag (XR/event-system quirk) must never
        /// freeze the story for good. A real draft holds it indefinitely.</summary>
        float _emptyFocusSince = -1f;
        const float EmptyFocusHoldSeconds = 15f;

        void Update()
        {
            // Tell the subtitle presenter while the user is engaged in an AI
            // exchange: input focused or holding draft text, voice listening,
            // a transcription running, or a question still awaiting an answer.
            // The scripted story stays paused until the reply has fully played.
            if (subtitles != null)
            {
                bool composing = false;
                if (chatWindow != null && chatWindow.activeSelf &&
                    questionInput != null)
                {
                    if (questionInput.text.Length > 0)
                    {
                        composing = true;
                        _emptyFocusSince = -1f;
                    }
                    else if (questionInput.isFocused)
                    {
                        if (_emptyFocusSince < 0f)
                            _emptyFocusSince = Time.unscaledTime;
                        composing = Time.unscaledTime - _emptyFocusSince <
                            EmptyFocusHoldSeconds;
                    }
                    else _emptyFocusSince = -1f;
                }
                else _emptyFocusSince = -1f;
                bool interacting = composing ||
                    _listening ||
                    (voiceInput != null &&
                     (voiceInput.Listening || voiceInput.Busy)) ||
                    (tutor != null && tutor.IsBusy);
                subtitles.aiInteracting = interacting;
            }
            // Reflect the voice loop's own state on the mic button each frame -
            // red while recording OR transcribing so the user always sees it work.
            if (voiceInput == null) return;
            bool live = voiceInput.Listening || voiceInput.Busy;
            if (live != _lastLive)
            {
                _lastLive = live;
                UpdateMicVisual();
            }
        }
        bool _lastLive;

        void OnDestroy() => StopDictationSafe();

        void StopDictationSafe()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            if (_dictation != null)
            {
                _dictation.Dispose();
                _dictation = null;
            }
#endif
        }

        public void Ask()
        {
            if (tutor == null || questionInput == null) return;
            string q = questionInput.text;
            if (string.IsNullOrWhiteSpace(q)) return;
            SetAnswer(ExperienceSettings.IsChinese ? "Rick：思考中……"
                : ExperienceSettings.IsDutch ? "Rick: aan het denken..."
                : "Rick: thinking...");
            tutor.Ask(q);
            questionInput.text = "";
            // Drop focus so the AI hold is driven only by the pending answer -
            // a focus that never releases would freeze the story for good.
            questionInput.DeactivateInputField();
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current
                    .SetSelectedGameObject(null);
        }

        public void ShowAnswer(string answer)
        {
            // The finished utterance is spoken exactly once - the "thinking"
            // placeholder above never reaches the voice.
            SetAnswer((ExperienceSettings.IsChinese ? "Rick：" : "Rick: ") + answer,
                answer);
        }

        public void SetAnswer(string s, string voiceText = null)
        {
            // Answers and the thinking state arrive through the shared subtitle
            // presenter, which also owns the voice so each utterance is spoken
            // once; the composer itself holds no answer widget.
            if (subtitles != null) { subtitles.ShowAnswer(s, voiceText); return; }
            if (answerText != null)
            {
                if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(answerText);
                answerText.text = s;
            }
            if (voice != null && !string.IsNullOrEmpty(voiceText))
                voice.Speak(voiceText);
        }
    }
}
