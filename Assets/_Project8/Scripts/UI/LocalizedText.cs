using UnityEngine;
using UnityEngine.UI;

namespace Project8.UI
{
    [RequireComponent(typeof(Text))]
    public class LocalizedText : MonoBehaviour
    {
        public string english;
        public string chinese;
        public string dutch;
        static Font _chineseFont;

        void OnEnable()
        {
            var label = GetComponent<Text>();
            if (ExperienceSettings.IsChinese && !string.IsNullOrEmpty(chinese))
            {
                UseChineseFont(label);
                label.text = chinese;
            }
            else if (ExperienceSettings.IsDutch && !string.IsNullOrEmpty(dutch))
                label.text = dutch;
            else if (!string.IsNullOrEmpty(english)) label.text = english;
        }

        public static void UseChineseFont(Text label)
        {
            if (_chineseFont == null)
                _chineseFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial" }, 24);
            if (_chineseFont != null) label.font = _chineseFont;
        }
    }
}
