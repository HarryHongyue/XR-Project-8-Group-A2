using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project8.UI
{
    public class ExperienceSettings : MonoBehaviour
    {
        public static bool IsChinese { get; private set; }
        public static bool IsDutch { get; private set; }
        public static bool HasChosen { get; private set; }
        public Text selectionText;
        public Text chineseButtonText;
        public Button enterButton;

        void Start()
        {
            if (enterButton != null) enterButton.interactable = false;
            if (chineseButtonText != null) LocalizedText.UseChineseFont(chineseButtonText);
            if (selectionText != null)
                selectionText.text = "Select a language / 请选择语言 / Kies een taal";
        }

        public void ChooseEnglish() => Choose(0);
        public void ChooseChinese() => Choose(1);
        public void ChooseDutch() => Choose(2);

        void Choose(int language)
        {
            IsChinese = language == 1;
            IsDutch = language == 2;
            HasChosen = true;
            if (selectionText != null)
            {
                selectionText.text = IsChinese ? "已选择中文。准备进入飞船。"
                    : IsDutch ? "Nederlands geselecteerd. Klaar om aan boord te gaan."
                    : "English selected. Ready to board.";
                if (IsChinese) LocalizedText.UseChineseFont(selectionText);
            }
            if (enterButton != null)
            {
                enterButton.interactable = true;
                var label = enterButton.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = IsChinese ? "开始体验"
                        : IsDutch ? "START DE ERVARING" : "ENTER EXPERIENCE";
                    if (IsChinese) LocalizedText.UseChineseFont(label);
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
