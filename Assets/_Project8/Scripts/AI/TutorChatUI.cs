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
        public Text answerText;
        public RickVoice voice;
        [Header("Avatar + voice")]
        public GameObject chatWindow;
        public Button micButton;
        public Text micLabel;

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
            if (micButton != null && !speechOk) micButton.interactable = false;
            UpdateMicVisual();
        }

        /// <summary>Avatar click: open or close the chat window.</summary>
        public void ToggleChat()
        {
            if (chatWindow != null) chatWindow.SetActive(!chatWindow.activeSelf);
        }

        /// <summary>Voice input button at the right edge of the input row.</summary>
        public void ToggleVoiceInput()
        {
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
            if (micLabel != null)
                micLabel.text = _listening ? "..." : "MIC";
            if (micButton != null)
            {
                var colors = micButton.colors;
                colors.normalColor = _listening
                    ? new Color(0.9f, 0.35f, 0.35f, 0.9f)
                    : new Color(0.35f, 0.55f, 0.75f, 0.55f);
                micButton.colors = colors;
            }
        }

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
            SetAnswer((ExperienceSettings.IsChinese ? "Rick：" : "Rick: ") + answer);
        }

        void SetAnswer(string s)
        {
            if (answerText == null) return;
            if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(answerText);
            answerText.text = s;
            if (voice != null) voice.Speak(s);
        }
    }
}
