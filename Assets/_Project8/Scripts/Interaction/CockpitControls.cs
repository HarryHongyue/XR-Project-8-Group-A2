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
        /// <summary>Desktop only: Tab toggles the mouse between UI mode (cursor
        /// visible and free, no view movement) and look mode (cursor hidden and
        /// locked, mouse steers the view). Opening any drawer frees the cursor.</summary>
        bool _cursorFree = false; // Tab once: cursor appears. Tab again: look mode.
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
        Coroutine _guidedLookRoutine;
        Coroutine _levelLookRoutine;
        /// <summary>Accumulated downward tilt the guided tour applied to the XR
        /// rig (degrees). The cockpit hull stays level - only the user's view
        /// pitches - and <see cref="ResetLookTilt"/> eases it back to zero.</summary>
        float _guidedPitch;

        void Start()
        {
            // Apply the one-face glass theme and readable button states to old
            // serialized scenes too, including Welcome (which has no StoryDirector).
            Project8.UI.RuntimeCockpitTheme.Apply(transform);
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
            // XRUIInputModule handles EVERY pointer source - mouse, touch, pen and
            // the XR ray interactors - so it stays enabled in both modes and the
            // panels remain clickable on the mirrored desktop view during VR runs.
            if (xrInputModule != null) xrInputModule.enabled = true;
            if (desktopInputModule != null) desktopInputModule.enabled = false;
            if (_deviceSim != null) _deviceSim.SetActive(_desktop);
            ApplyCursorState();
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
        /// <summary>Non-toggling open used by guided tasks (prediction) that
        /// must bring the drawer up without risking closing an open one.</summary>
        public void OpenControls() => SetDrawer(Drawer.Controls);
        public void ClosePanels() => SetDrawer(Drawer.None);
        public void GoToStory() => UnityEngine.SceneManagement.SceneManager.LoadScene("Main");

        /// <summary>Gently aims the cockpit toward the current tour component.
        /// The turn is brief; flight controls remain enabled and manual turning
        /// cancels the guidance immediately.</summary>
        public void GuideLookAt(Vector3 worldTarget)
        {
            GuideLookAt(worldTarget, null);
        }

        /// <summary>Same guidance, but also glides the ship toward `vantage` -
        /// a viewing standoff the tour computes beside each component - so the
        /// camera ends up facing the part instead of only yawing toward it.
        /// Any user input cancels both the turn and the glide at once.</summary>
        public void GuideLookAt(Vector3 worldTarget, Vector3? vantage)
        {
            if (seatedCamera == null) return;
            // A fresh aim keeps whatever tilt is already applied - the level
            // routine would fight the new guidance, so it is stopped first.
            if (_levelLookRoutine != null)
            {
                StopCoroutine(_levelLookRoutine);
                _levelLookRoutine = null;
            }
            if (_guidedLookRoutine != null) StopCoroutine(_guidedLookRoutine);
            _guidedLookRoutine = StartCoroutine(GuideLookRoutine(worldTarget, vantage));
        }

        System.Collections.IEnumerator GuideLookRoutine(Vector3 worldTarget, Vector3? vantage)
        {
            float elapsed = 0f;
            const float duration = 1.5f;
            while (elapsed < duration && seatedCamera != null)
            {
                float dt = Time.unscaledDeltaTime;

                // Glide toward the vantage point (lab-clamped); the seat rides
                // with the ship, so this IS the "camera move" the tour wants.
                if (vantage.HasValue)
                {
                    Vector3 v = vantage.Value;
                    if (story != null && story.InLab)
                        v = story.ClampLabPosition(v);
                    transform.position = Vector3.MoveTowards(
                        transform.position, v, 2.2f * dt);
                }

                Vector3 toTarget = worldTarget - seatedCamera.transform.position;
                Vector3 flat = toTarget;
                flat.y = 0f;
                Vector3 forward = transform.forward;
                forward.y = 0f;
                if (flat.sqrMagnitude > 0.001f && forward.sqrMagnitude > 0.001f)
                {
                    float angle = Vector3.SignedAngle(forward, flat, Vector3.up);
                    float turn = Mathf.Clamp(angle, -140f * dt, 140f * dt);
                    transform.RotateAround(seatedCamera.transform.position, Vector3.up, turn);
                }

                if (_desktop)
                {
                    Vector3 local = transform.InverseTransformDirection(
                        worldTarget - seatedCamera.transform.position);
                    if (local.z > 0.01f)
                    {
                        // _pitch convention: NEGATIVE = look up (mouse-look
                        // subtracts delta.y). A target below the eye (local.y
                        // < 0) must therefore produce a POSITIVE pitch - the
                        // negated y is what tilts the view DOWN onto parts.
                        float pitch = Mathf.Atan2(-local.y, local.z) * Mathf.Rad2Deg;
                        _pitch = Mathf.MoveTowards(_pitch,
                            Mathf.Clamp(pitch, -60f, 70f), 90f * dt);
                        seatedCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
                    }
                }
                else if (xrRigRoot != null)
                {
                    // XR: the tracked head cannot be pitched directly, so tilt
                    // the RIG around the eye until the user's real view drops
                    // onto the part. The hull and every panel stay level; only
                    // the tracked space (camera + controllers) tilts.
                    Vector3 eye = seatedCamera.transform.position;
                    Vector3 headFwd = seatedCamera.transform.forward;
                    Vector3 dir = (worldTarget - eye).normalized;
                    float headPitch = Mathf.Asin(Mathf.Clamp(headFwd.y, -1f, 1f)) *
                        Mathf.Rad2Deg;
                    float flatLen = new Vector3(dir.x, 0f, dir.z).magnitude;
                    float wantPitch = Mathf.Atan2(dir.y, flatLen) * Mathf.Rad2Deg;
                    float dPitch = Mathf.Clamp(wantPitch - headPitch,
                        -120f * dt, 120f * dt);
                    if (Mathf.Abs(dPitch) > 0.01f)
                    {
                        // Axis = the camera's horizontal RIGHT: a positive
                        // rotation about it pitches the view downward.
                        Vector3 axis = Vector3.Cross(Vector3.up, headFwd);
                        axis.y = 0f;
                        if (axis.sqrMagnitude > 0.001f)
                        {
                            axis.Normalize();
                            xrRigRoot.RotateAround(eye, axis, -dPitch);
                            _guidedPitch -= dPitch; // applied rig tilt, +=down
                        }
                    }
                }

                elapsed += dt;
                yield return null;
            }
            _guidedLookRoutine = null;
        }

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
            // Any drawer needs a usable pointer - free the cursor until Tab locks it.
            if (_desktop && drawer != Drawer.None) _cursorFree = true;
            ApplyCursorState();
        }

        void ApplyCursorState()
        {
            if (!_desktop)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
                return;
            }
            Cursor.visible = _cursorFree;
            Cursor.lockState = _cursorFree ? CursorLockMode.None : CursorLockMode.Locked;
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
                // Any deliberate thrust also cancels a tour glide - the user's
                // hand always wins over the camera guide.
                if (move.sqrMagnitude > 0.04f || Mathf.Abs(turn.y) > 0.15f)
                    CancelGuidedLook();
                Vector3 fwd = transform.forward; fwd.y = 0f;
                Vector3 rgt = transform.right; rgt.y = 0f;
                if (fwd.sqrMagnitude > 0.01f)
                {
                    fwd.Normalize(); rgt.Normalize();
                    transform.position += (fwd * move.y + rgt * move.x +
                        Vector3.up * turn.y * 0.6f) * flightSpeed * Time.deltaTime;
                }
                if (Mathf.Abs(turn.x) > 0.15f)
                {
                    CancelGuidedLook();
                    transform.RotateAround(seatedCamera.transform.position, Vector3.up,
                        turn.x * 90f * Time.deltaTime);
                }
                if (story != null && story.InLab)
                    transform.position = story.ClampLabPosition(transform.position);
            }

            if (!_desktop || Keyboard.current == null) return;
            var keys = Keyboard.current;
            // Tab trades the mouse between "point at UI" and "steer the view":
            // freed, the cursor is visible and the view stands still; locked,
            // the cursor hides and the mouse flies the ship.
            if (keys.tabKey.wasPressedThisFrame)
            {
                _cursorFree = !_cursorFree;
                ApplyCursorState();
            }
            if (keys.mKey.wasPressedThisFrame) TogglePanel();
            if (keys.escapeKey.wasPressedThisFrame)
            {
                ClosePanels();
                _cursorFree = true;
                ApplyCursorState();
            }
            // Desktop Enter advances the story only - it never opens panels.
            if (keys.enterKey.wasPressedThisFrame && story != null)
                story.Advance();
            bool labFreeFlight = story != null && story.InLab;
            bool piloting = (_drawer == Drawer.None || labFreeFlight) &&
                            (story == null || story.CanPilot);
            if (piloting)
            {
                Vector3 movement = Vector3.zero;
                if (keys.wKey.isPressed) movement += transform.forward;
                if (keys.sKey.isPressed) movement -= transform.forward;
                if (keys.aKey.isPressed) movement -= transform.right;
                if (keys.dKey.isPressed) movement += transform.right;
                if (keys.eKey.isPressed || keys.spaceKey.isPressed) movement += Vector3.up;
                if (keys.qKey.isPressed || keys.leftCtrlKey.isPressed) movement -= Vector3.up;
                if (movement != Vector3.zero) CancelGuidedLook();
                float speed = flightSpeed *
                    ((keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed) ? boostMultiplier : 1f);
                transform.position += Vector3.ClampMagnitude(movement, 1f) * speed * Time.deltaTime;
                if (labFreeFlight && story != null)
                    transform.position = story.ClampLabPosition(transform.position);
            }

            // Mouse-look runs only while the cursor is locked (Tab toggles). The
            // camera is bolted to the cockpit: horizontal motion yaws the whole
            // SHIP around the seat so every panel stays dead ahead, vertical
            // motion pitches only the camera. No free-floating crosshair drift -
            // either you're steering the ship or you're pointing at a panel.
            if (_cursorFree || Mouse.current == null || seatedCamera == null) return;
            Vector2 delta = Mouse.current.delta.ReadValue() * lookSensitivity;
            if (delta.sqrMagnitude > 0.01f) CancelGuidedLook();
            transform.RotateAround(seatedCamera.transform.position, Vector3.up, delta.x);
            if (labFreeFlight && story != null)
                transform.position = story.ClampLabPosition(transform.position);
            _pitch = Mathf.Clamp(_pitch - delta.y, -60f, 70f);
            seatedCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void CancelGuidedLook()
        {
            if (_guidedLookRoutine == null) return;
            StopCoroutine(_guidedLookRoutine);
            _guidedLookRoutine = null;
            // The user took over - level the rig again so their own view is
            // not left tilted by a half-finished guided look.
            ResetLookTilt();
        }

        /// <summary>Ease the XR rig back to an untilted view. Called when the
        /// tour ends, resets or the user grabs manual control mid-guidance.</summary>
        public void ResetLookTilt()
        {
            if (Mathf.Abs(_guidedPitch) < 0.01f) { _guidedPitch = 0f; return; }
            if (_levelLookRoutine == null)
                _levelLookRoutine = StartCoroutine(LevelLookRoutine());
        }

        System.Collections.IEnumerator LevelLookRoutine()
        {
            while (Mathf.Abs(_guidedPitch) > 0.05f && xrRigRoot != null &&
                   seatedCamera != null)
            {
                float step = Mathf.Clamp(_guidedPitch,
                    -120f * Time.unscaledDeltaTime, 120f * Time.unscaledDeltaTime);
                Vector3 eye = seatedCamera.transform.position;
                Vector3 headFwd = seatedCamera.transform.forward;
                Vector3 axis = Vector3.Cross(Vector3.up, headFwd);
                axis.y = 0f;
                if (axis.sqrMagnitude < 0.001f) break;
                axis.Normalize();
                xrRigRoot.RotateAround(eye, axis, -step);
                _guidedPitch -= step;
                yield return null;
            }
            _guidedPitch = 0f;
            _levelLookRoutine = null;
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
