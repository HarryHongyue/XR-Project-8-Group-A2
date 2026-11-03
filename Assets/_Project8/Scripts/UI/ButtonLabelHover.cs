using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project8.UI
{
    /// <summary>
    /// Keeps labels white across pointer and keyboard states. The glass face
    /// carries the subtle hover/press change; flipping the label to dark blue
    /// made it disappear on the dim blue background.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ButtonLabelHover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        public Text label;
        public Color labelNormal = Color.white;
        public Color labelHot = Color.white;

        bool _hot;

        public void OnPointerEnter(PointerEventData d) { _hot = true; Apply(); }
        public void OnPointerExit(PointerEventData d) { _hot = false; Apply(); }
        public void OnPointerDown(PointerEventData d) { _hot = true; Apply(); }
        public void OnPointerUp(PointerEventData d) { _hot = false; Apply(); }
        public void OnSelect(BaseEventData d) { _hot = true; Apply(); }
        public void OnDeselect(BaseEventData d) { _hot = false; Apply(); }
        void OnDisable() { _hot = false; Apply(); }
        void OnEnable() { Apply(); }

        /// <summary>External "selected" state hook kept for toggle-like buttons;
        /// only the face tint changes, the label remains white.</summary>
        public void SetHot(bool on) { _hot = on; Apply(); }

        void Apply()
        {
            if (label != null) label.color = Color.white;
        }
    }
}
