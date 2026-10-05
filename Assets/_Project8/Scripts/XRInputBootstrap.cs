using UnityEngine;
using UnityEngine.InputSystem;

namespace Project8
{
    /// <summary>
    /// Enables the project-wide input actions at startup. In Input System 1.20 the
    /// standalone "InputActionManager" component was removed; the supported mechanism
    /// is the global InputSystem.actions asset plus Enable(). The scene builder wires
    /// this component to the XRI Default Input Actions asset from the Starter Assets
    /// sample so every interactor action reference receives input.
    /// </summary>
    public class XRInputBootstrap : MonoBehaviour
    {
        public InputActionAsset actions;

        void Awake()
        {
            if (actions == null) return;
            // InputSystem.actions may only be assigned in edit mode; at runtime the
            // asset itself is enabled directly, which activates every map it contains.
            actions.Enable();
        }

        void OnEnable() => actions?.Enable();
    }
}
