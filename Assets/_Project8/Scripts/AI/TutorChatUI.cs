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
            if (chatWindow != null) chatWindow.SetActive(false);
            bool speechOk = Application.platform == RuntimePlatform.WindowsEditor ||
                            Application.platform == RuntimePlatform.WindowsPlayer;
            if (micButton != null && !speechOk && voiceInput == null)
                micButton.interactable = false;
            UpdateMicVisual();
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
            bool live = _listening || (voiceInput != null && voiceInput.Listening);
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
            // Reflect the voice loop's own state on the mic button each frame.
            if (voiceInput != null && voiceInput.Listening != _lastLive)
            {
                _lastLive = voiceInput.Listening;
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
            tutor.Ask(q);
            questionInput.text = "";
            SetAnswer(ExperienceSettings.IsChinese ? "Rick：思考中……"
                : ExperienceSettings.IsDutch ? "Rick: aan het denken..."
                : "Rick: thinking...");
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
