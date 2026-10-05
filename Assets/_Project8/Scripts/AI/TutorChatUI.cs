using UnityEngine;
using UnityEngine.UI;
using Project8.UI;

namespace Project8.AI
{
    /// <summary>
    /// Thin UI glue for AITutor: a question input box + ASK button. Answers land in
    /// answerText (the mission panel's narration box is reused so Rick's scripted
    /// lines and live answers share one place).
    /// </summary>
    public class TutorChatUI : MonoBehaviour
    {
        public AITutor tutor;
        public InputField questionInput;
        public Text answerText;

        void Start()
        {
            if (questionInput == null) return;
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
        }
    }
}
