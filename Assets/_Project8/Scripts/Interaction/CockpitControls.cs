using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR;
using Project8.Story;
using System.Linq;

namespace Project8.Interaction
{
    public class CockpitControls : MonoBehaviour
    {
        public Camera seatedCamera;
        public GameObject controlPanel;
        public GameObject compactPanel;
        public GameObject secondaryPanel;
        public StoryDirector story;
        public float flightSpeed = 5f;
        public float boostMultiplier = 3f;
        public float lookSensitivity = 0.12f;
        public Behaviour xrInputModule;
        public InputSystemUIInputModule desktopInputModule;
        [Tooltip("XR rig root that gets rotated at startup so the user's initial view " +
                 "faces the ship forward (+Z), regardless of their guardian orientation.")]
        public Transform xrRigRoot;

        enum Drawer { None, Secondary, Controls }
        Drawer _drawer;
        bool _desktop;
        float _yaw;
        float _pitch;

        /// <summary>In XR the user's physical facing rarely matches the rig's +Z, so
        /// everything built "ahead" of the ship (panels, black holes) can sit behind
        /// them. Rotate the rig around the camera position once tracking settles so
        /// forward really is forward.</summary>
        System.Collections.IEnumerator RecenterToShipForward()
        {
            yield return new WaitForSeconds(0.6f); // let the first real pose arrive
            for (int i = 0; i < 30; i++)
            {
                if (xrRigRoot == null || seatedCamera == null) yield break;
                Vector3 have = seatedCamera.transform.forward;
                Vector3 want = transform.forward;
                have.y = 0f; want.y = 0f;
                if (have.sqrMagnitude < 0.01f) { yield return null; continue; }
                float yaw = Vector3.SignedAngle(have.normalized, want.normalized, Vector3.up);
                xrRigRoot.RotateAround(seatedCamera.transform.position, Vector3.up, yaw);
                if (Mathf.Abs(yaw) < 0.5f) yield break;
                yield return null;
            }
        }

        Behaviour[] _poseDrivers;
        GameObject _deviceSim;

        void Start()
        {
            if (seatedCamera != null)
                _poseDrivers = seatedCamera.GetComponents<Behaviour>()
                    .Where(b => b.GetType().Name.Contains("TrackedPoseDriver")).ToArray();
            // The device simulator is spawned inactive; it only comes alive when
            // this settles into desktop mode.
            foreach (var root in UnityEngine.SceneManagement.SceneManager
                         .GetActiveScene().GetRootGameObjects())
                if (root.name.IndexOf("Device Simulator",
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                    _deviceSim = root;
            StartCoroutine(DetectXRThenConfigure());
            ClosePanels();
        }

        /// <summary>XR init can lag behind the first scene's Start(): deciding the
        /// desktop/XR mode from a single isDeviceActive read misconfigures BOTH
        /// directions - in VR it leaves the XR UI input module disabled (rays can
        /// never click) and lets the keyboard/mouse device simulator swallow real
        /// headset input. Poll for a few seconds, then commit.</summary>
        System.Collections.IEnumerator DetectXRThenConfigure()
        {
            for (int i = 0; i < 8 && !XRSettings.isDeviceActive; i++)
                yield return new WaitForSeconds(0.5f);
            _desktop = !XRSettings.isDeviceActive;
            ConfigureForMode();
            if (!_desktop) StartCoroutine(RecenterToShipForward());
        }

        void ConfigureForMode()
        {
            if (_poseDrivers != null)
                foreach (var d in _poseDrivers)
                    if (d != null) d.enabled = !_desktop;
            if (_desktop && seatedCamera != null)
            {
                seatedCamera.transform.localPosition = Vector3.zero;
                seatedCamera.transform.localRotation = Quaternion.identity;
            }
            if (xrInputModule != null) xrInputModule.enabled = !_desktop;
            if (desktopInputModule != null) desktopInputModule.enabled = _desktop;
            if (_deviceSim != null) _deviceSim.SetActive(_desktop);
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
        public void GoToStory() => UnityEngine.SceneManagement.SceneManager.LoadScene("Main");

        void SetDrawer(Drawer drawer)
        {
            _drawer = drawer;
            if (controlPanel != null && controlPanel == secondaryPanel)
            {
                // Merged panel (Wave Lab): one object serves both drawers.
                controlPanel.SetActive(drawer != Drawer.None);
            }
            else
            {
                if (controlPanel != null) controlPanel.SetActive(drawer == Drawer.Controls);
                if (secondaryPanel != null) secondaryPanel.SetActive(drawer == Drawer.Secondary);
            }
            if (compactPanel != null) compactPanel.SetActive(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        void Update()
        {
            // ---- VR thumbstick flight (runs before the desktop early-out) --------
            // The head is pure free-look (TrackedPoseDriver owns it) and NEVER
            // steers the ship. Left stick translates along the SHIP's own axes so
            // pushing forward always flies nose-forward no matter where the user
            // is looking; right stick X yaws the hull around the user's head so
            // the seat stays put instead of swinging on a long lever arm, and
            // right stick Y lifts/lowers the ship.
            if (!_desktop && (story == null || story.CanPilot) && seatedCamera != null)
            {
                Vector2 move = Vector2.zero, turn = Vector2.zero;
                var leftDev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
                var rightDev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
                if (leftDev.isValid)
                    leftDev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out move);
                if (rightDev.isValid)
                    rightDev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out turn);
                // Fallback: some runtimes only expose the stick through the
                // Input System's dynamic XRController layout.
                if (move == Vector2.zero)
                    move = ReadStick(UnityEngine.InputSystem.XR.XRController.leftHand);
                if (turn == Vector2.zero)
                    turn = ReadStick(UnityEngine.InputSystem.XR.XRController.rightHand);
                Vector3 fwd = transform.forward; fwd.y = 0f;
                Vector3 rgt = transform.right; rgt.y = 0f;
                if (fwd.sqrMagnitude > 0.01f)
                {
                    fwd.Normalize(); rgt.Normalize();
                    transform.position += (fwd * move.y + rgt * move.x +
                        Vector3.up * turn.y * 0.6f) * flightSpeed * Time.deltaTime;
                }
                if (Mathf.Abs(turn.x) > 0.15f)
                    transform.RotateAround(seatedCamera.transform.position, Vector3.up,
                        turn.x * 90f * Time.deltaTime);
            }

            if (!_desktop || Keyboard.current == null) return;
            var keys = Keyboard.current;
            if (keys.mKey.wasPressedThisFrame) TogglePanel();
            if (keys.escapeKey.wasPressedThisFrame) ClosePanels();
            if (keys.enterKey.wasPressedThisFrame && story != null)
            {
                if (_drawer != Drawer.Secondary) ShowMission();
                story.Advance();
            }
            bool piloting = _drawer == Drawer.None && (story == null || story.CanPilot);
            if (piloting)
            {
                Vector3 movement = Vector3.zero;
                if (keys.wKey.isPressed) movement += transform.forward;
                if (keys.sKey.isPressed) movement -= transform.forward;
                if (keys.aKey.isPressed) movement -= transform.right;
                if (keys.dKey.isPressed) movement += transform.right;
                if (keys.eKey.isPressed || keys.spaceKey.isPressed) movement += Vector3.up;
                if (keys.qKey.isPressed || keys.leftCtrlKey.isPressed) movement -= Vector3.up;
                float speed = flightSpeed *
                    ((keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed) ? boostMultiplier : 1f);
                transform.position += Vector3.ClampMagnitude(movement, 1f) * speed * Time.deltaTime;
            }

            // Free mouse-look in desktop mode: the view follows the mouse directly,
            // no button hold required. Paused while the pointer is over UI so panel
            // clicks never rotate the view. While piloting, horizontal motion yaws
            // the SHIP so WASD stays aligned; during story cinematics it only turns
            // the head so the ship keeps its choreographed course. In VR the
            // TrackedPoseDriver owns the camera instead (real head tracking).
            if (Mouse.current == null || seatedCamera == null) return;
            bool overUI = UnityEngine.EventSystems.EventSystem.current != null &&
                          UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            if (overUI) return;
            Vector2 delta = Mouse.current.delta.ReadValue() * lookSensitivity;
            if (piloting) transform.Rotate(0f, delta.x, 0f, Space.World);
            else _yaw = Mathf.Clamp(_yaw + delta.x, -80f, 80f);
            _pitch = Mathf.Clamp(_pitch - delta.y, -60f, 70f);
            seatedCamera.transform.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        static Vector2 ReadStick(UnityEngine.InputSystem.XR.XRController controller)
        {
            if (controller == null) return Vector2.zero;
            var stick = controller.TryGetChildControl<
                            UnityEngine.InputSystem.Controls.Vector2Control>("thumbstick")
                        ?? controller.TryGetChildControl<
                            UnityEngine.InputSystem.Controls.Vector2Control>("joystick");
            return stick != null ? stick.ReadValue() : Vector2.zero;
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
