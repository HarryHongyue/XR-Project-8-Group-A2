using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR;
using Project8.Story;

namespace Project8.Interaction
{
    public class CockpitControls : MonoBehaviour
    {
        public Camera seatedCamera;
        public GameObject controlPanel;
        public GameObject compactPanel;
        public GameObject secondaryPanel;
        public StoryDirector story;
        public float flightSpeed = 3f;
        public float lookSensitivity = 0.12f;
        public Behaviour xrInputModule;
        public InputSystemUIInputModule desktopInputModule;

        enum Drawer { None, Secondary, Controls }
        Drawer _drawer;
        bool _desktop;
        float _yaw;
        float _pitch;

        void Start()
        {
            _desktop = !XRSettings.isDeviceActive;
            if (_desktop && seatedCamera != null)
            {
                foreach (var component in seatedCamera.GetComponents<Behaviour>())
                    if (component.GetType().Name.Contains("TrackedPoseDriver")) component.enabled = false;
                seatedCamera.transform.localPosition = Vector3.zero;
                seatedCamera.transform.localRotation = Quaternion.identity;
            }
            if (xrInputModule != null) xrInputModule.enabled = !_desktop;
            if (desktopInputModule != null) desktopInputModule.enabled = _desktop;
            ClosePanels();
        }

        public void TogglePanel() => ShowControls();
        public void SetPanel(bool expanded)
        {
            if (expanded) ShowControls();
            else ClosePanels();
        }

        public void ShowMission() => SetDrawer(_drawer == Drawer.Secondary ? Drawer.None : Drawer.Secondary);
        public void OpenMission() => SetDrawer(Drawer.Secondary);
        public void ShowControls() => SetDrawer(_drawer == Drawer.Controls ? Drawer.None : Drawer.Controls);
        public void ClosePanels() => SetDrawer(Drawer.None);

        void SetDrawer(Drawer drawer)
        {
            _drawer = drawer;
            if (controlPanel != null) controlPanel.SetActive(drawer == Drawer.Controls);
            if (secondaryPanel != null) secondaryPanel.SetActive(drawer == Drawer.Secondary);
            if (compactPanel != null) compactPanel.SetActive(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        void Update()
        {
            if (!_desktop || Keyboard.current == null) return;
            var keys = Keyboard.current;
            if (keys.mKey.wasPressedThisFrame) TogglePanel();
            if (keys.escapeKey.wasPressedThisFrame) ClosePanels();
            if (keys.enterKey.wasPressedThisFrame && story != null)
            {
                if (_drawer != Drawer.Secondary) ShowMission();
                story.Advance();
            }
            if (_drawer != Drawer.None || story != null && !story.CanPilot) return;

            Vector3 movement = Vector3.zero;
            if (keys.wKey.isPressed) movement += transform.forward;
            if (keys.sKey.isPressed) movement -= transform.forward;
            if (keys.aKey.isPressed) movement -= transform.right;
            if (keys.dKey.isPressed) movement += transform.right;
            if (keys.eKey.isPressed) movement += Vector3.up;
            if (keys.qKey.isPressed) movement -= Vector3.up;
            transform.position += Vector3.ClampMagnitude(movement, 1f) * flightSpeed * Time.deltaTime;

            if (Mouse.current == null || !Mouse.current.rightButton.isPressed || seatedCamera == null) return;
            Vector2 delta = Mouse.current.delta.ReadValue() * lookSensitivity;
            _yaw += delta.x;
            _pitch = Mathf.Clamp(_pitch - delta.y, -60f, 70f);
            seatedCamera.transform.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void LateUpdate()
        {
            if (seatedCamera == null) return;
            if (_desktop) seatedCamera.transform.localPosition = Vector3.zero;
            else
            {
                Vector3 p = seatedCamera.transform.localPosition;
                seatedCamera.transform.localPosition = new Vector3(
                    Mathf.Clamp(p.x, -0.22f, 0.22f), p.y,
                    Mathf.Clamp(p.z, -0.22f, 0.22f));
            }
        }
    }
}
