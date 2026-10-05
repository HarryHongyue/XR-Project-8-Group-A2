using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project8.UI
{
    public class ExperienceSettings : MonoBehaviour
    {
        public static bool IsChinese { get; private set; }
        public Text selectionText;
        public Text chineseButtonText;
        public Button enterButton;

        void Start()
        {
            if (enterButton != null) enterButton.interactable = false;
            if (chineseButtonText != null) LocalizedText.UseChineseFont(chineseButtonText);
            if (selectionText != null) selectionText.text = "Select a language / 请选择语言";
        }

        public void ChooseEnglish() => Choose(false);
        public void ChooseChinese() => Choose(true);

        void Choose(bool chinese)
        {
            IsChinese = chinese;
            if (selectionText != null)
            {
                selectionText.text = chinese ? "已选择中文。准备进入飞船。" : "English selected. Ready to board.";
                if (chinese) LocalizedText.UseChineseFont(selectionText);
            }
            if (enterButton != null)
            {
                enterButton.interactable = true;
                var label = enterButton.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = chinese ? "开始体验" : "ENTER EXPERIENCE";
                    if (chinese) LocalizedText.UseChineseFont(label);
                }
            }
        }

        public void BeginExperience()
        {
            if (enterButton != null && enterButton.interactable)
                SceneManager.LoadScene("Main");
        }
    }
}
