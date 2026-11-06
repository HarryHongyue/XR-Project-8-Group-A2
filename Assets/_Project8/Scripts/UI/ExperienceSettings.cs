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
        [Tooltip("Language buttons - assigned by the scene builder or resolved " +
                 "by name at runtime in older serialized scenes.")]
        public Button englishButton, chineseButton, dutchButton;

        // Selected look: the WHOLE button face fills with a bright accent cyan
        // (every Button state colour is replaced so hover/press can't wash it
        // out) plus dark label ink, a scale pop and the pulsing spotlight ring.
        static readonly Color SelectedFace  = new Color(0.32f, 0.78f, 1f, 0.95f);
        static readonly Color SelectedHot   = new Color(0.55f, 0.92f, 1f, 1f);
        static readonly Color SelectedPress = new Color(0.20f, 0.60f, 0.85f, 1f);
        static readonly Color SelectedInk   = new Color(0.02f, 0.09f, 0.16f, 1f);
        static readonly Color NormalFace    = new Color(0.16f, 0.24f, 0.36f, 0.55f); // GlassButtonTint

        void Start()
        {
            if (enterButton != null) enterButton.interactable = false;
            if (chineseButtonText != null) LocalizedText.UseChineseFont(chineseButtonText);
            if (selectionText != null)
                selectionText.text = "Select a language / 请选择语言 / Kies een taal";
            ResolveLanguageButtons();
        }

        /// <summary>Wire the three language buttons and give each a spotlight
        /// ring id. Serialized refs win; older scenes are found by name so the
        /// selected-language highlight works without a scene rebuild.</summary>
        void ResolveLanguageButtons()
        {
            englishButton  = englishButton  != null ? englishButton  : FindButton("Btn_English");
            chineseButton  = chineseButton  != null ? chineseButton  : FindButton("Btn_Chinese");
            dutchButton    = dutchButton    != null ? dutchButton    : FindButton("Btn_Dutch");
        }

        Button FindButton(string objectName)
        {
            foreach (var b in GetComponentsInChildren<Button>(true))
                if (b != null && b.gameObject.name == objectName) return b;
            return null;
        }

        /// <summary>Strong persistent feedback for the picked language: the
        /// whole face fills with accent cyan plus dark label ink and a small
        /// scale pop - NO pulsing ring here (user request); the spotlight
        /// system keeps working for the other buttons untouched.</summary>
        void HighlightLanguage(int language)
        {
            PaintButton(englishButton, language == 0);
            PaintButton(chineseButton, language == 1);
            PaintButton(dutchButton,   language == 2);
        }

        static void PaintButton(Button button, bool selected)
        {
            if (button == null) return;
            var face = button.targetGraphic as Graphic ?? button.GetComponent<Graphic>();
            var hover = button.GetComponent<ButtonLabelHover>();
            if (selected)
            {
                var colors = button.colors;
                colors.normalColor = SelectedFace;
                colors.highlightedColor = SelectedHot;
                colors.pressedColor = SelectedPress;
                colors.selectedColor = SelectedFace;
                button.colors = colors;
                if (face != null) face.color = SelectedFace;
                if (hover != null) { hover.labelNormal = SelectedInk; hover.labelHot = SelectedInk; }
                foreach (var label in button.GetComponentsInChildren<Text>(true))
                    label.color = SelectedInk;
                button.transform.localScale = Vector3.one * 1.08f;
            }
            else
            {
                var style = button.GetComponent<ReadableButtonStyle>();
                if (style != null) style.Apply();
                if (face != null) face.color = NormalFace;
                if (hover != null) { hover.labelNormal = Color.white; hover.labelHot = Color.white; }
                foreach (var label in button.GetComponentsInChildren<Text>(true))
                    label.color = Color.white;
                button.transform.localScale = Vector3.one;
            }
        }

        public void ChooseEnglish() => Choose(0);
        public void ChooseChinese() => Choose(1);
        public void ChooseDutch() => Choose(2);

        void Choose(int language)
        {
            IsChinese = language == 1;
            IsDutch = language == 2;
            HasChosen = true;
            HighlightLanguage(language);
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
