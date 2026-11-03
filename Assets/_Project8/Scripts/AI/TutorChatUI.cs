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
                if (rect != null) rect.anchoredPosition = new Vector2(-280f, -170f);
                chatWindow.SetActive(false);
            }
            bool speechOk = Application.platform == RuntimePlatform.WindowsEditor ||
                            Application.platform == RuntimePlatform.WindowsPlayer;
            if (micButton != null && !speechOk && voiceInput == null)
                micButton.interactable = false;
            UpdateMicVisual();
        }

        void EnsureRickFace()
        {
            Transform avatar = transform.Find("Avatar");
            if (avatar == null) return;

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
            if (micLabel != null)
                micLabel.text = live ? "..." : "MIC";
            if (micButton != null)
            {
                var colors = micButton.colors;
                colors.normalColor = live
                    ? new Color(0.9f, 0.35f, 0.35f, 0.9f)
                    : new Color(0.35f, 0.55f, 0.75f, 0.55f);
                micButton.colors = colors;
            }
        }

        void Update()
        {
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
