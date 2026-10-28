using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project8.UI
{
    /// <summary>
    /// The half of button feedback Unity's ColorTint transition cannot express:
    /// it can only tint the background, never the label. On hover/press/select
    /// this flips the label to deep cockpit blue while the Button's own tint
    /// brightens the glass face toward white - readable in BOTH directions.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ButtonLabelHover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        public Text label;
        public Color labelNormal = Color.white;
        public Color labelHot = new Color(0.02f, 0.25f, 0.45f); // deep cockpit blue

        bool _hot;

        public void OnPointerEnter(PointerEventData d) { _hot = true; Apply(); }
        public void OnPointerExit(PointerEventData d) { _hot = false; Apply(); }
        public void OnPointerDown(PointerEventData d) { _hot = true; Apply(); }
        public void OnPointerUp(PointerEventData d) { _hot = false; Apply(); }
        public void OnSelect(BaseEventData d) { _hot = true; Apply(); }
        public void OnDeselect(BaseEventData d) { _hot = false; Apply(); }
        void OnDisable() { _hot = false; Apply(); }
        void OnEnable() { Apply(); }

        void Apply()
        {
            if (label != null) label.color = _hot ? labelHot : labelNormal;
        }
    }
}
