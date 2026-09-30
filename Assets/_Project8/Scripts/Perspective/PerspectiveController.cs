using UnityEngine;
using UnityEngine.Events;
using Project8.Core;

namespace Project8.Perspective
{
    public enum PerspectiveMode { Observer, RideTheWave }

    /// <summary>
    /// Switches the user between a stationary observer frame and the deliberately slowed
    /// "ride-with-the-wave" frame. While riding, the rig root follows an anchor that
    /// travels at the visualization speed beside the wavefront, offset laterally so the
    /// test-object field stays in view.
    ///
    /// Comfort note: rotation is never overridden (only position is blended), which keeps
    /// the vestibular/visual system consistent and mitigates simulator sickness.
    /// </summary>
    public class PerspectiveController : MonoBehaviour
    {
        [Header("References")]
        public Transform xrRigRoot;
        public GravitationalWaveModel model;
        public SimulationController simulation;

        [Header("Ride pose")]
        [Tooltip("Lateral distance (metres) from the wavefront plane while riding.")]
        public float rideSideOffset = 3.5f;
        [Tooltip("Extra height above the rig's observer height while riding.")]
        public float rideHeightOffset = 0.4f;
        [Tooltip("Seconds to blend between frames.")]
        [Min(0.1f)] public float transitionSeconds = 1.5f;

        public PerspectiveMode Mode { get; private set; } = PerspectiveMode.Observer;
        public UnityEvent<PerspectiveMode> onModeChanged = new UnityEvent<PerspectiveMode>();

        Vector3 _observerPos;
        Quaternion _observerRot;
        float _blend; // 0 = observer, 1 = riding

        void Start()
        {
            if (xrRigRoot != null)
            {
                _observerPos = xrRigRoot.position;
                _observerRot = xrRigRoot.rotation;
            }
        }

        public void TogglePerspective()
            => SetMode(Mode == PerspectiveMode.Observer ? PerspectiveMode.RideTheWave
                                                        : PerspectiveMode.Observer);

        public void SetMode(PerspectiveMode mode)
        {
            if (Mode == mode) return;
            Mode = mode;
            if (mode == PerspectiveMode.RideTheWave && xrRigRoot != null)
            {
                // store the observer frame so "compare" can return exactly to it
                _observerPos = xrRigRoot.position;
                _observerRot = xrRigRoot.rotation;
            }
            onModeChanged.Invoke(mode);
        }

        void Update()
        {
            if (xrRigRoot == null) return;

            float target = Mode == PerspectiveMode.RideTheWave ? 1f : 0f;
            _blend = Mathf.MoveTowards(_blend, target, Time.deltaTime / transitionSeconds);
            if (Mathf.Approximately(_blend, 0f)) return;

            Vector3 ridePos = ComputeRidePosition();
            float k = Mathf.SmoothStep(0f, 1f, _blend);
            xrRigRoot.position = Vector3.Lerp(_observerPos, ridePos, k);
            xrRigRoot.rotation = Quaternion.Slerp(_observerRot, _observerRot, k); // yaw locked
        }

        Vector3 ComputeRidePosition()
        {
            Vector3 d = model != null ? model.PropagationDirection : Vector3.forward;
            Vector3 o = model != null ? model.Origin : Vector3.zero;
            float s = model != null
                ? model.WavefrontDistance(simulation != null ? simulation.SimulationTime : 0f)
                : 0f;

            model.GetTransverseAxes(out Vector3 u1, out _);
            Vector3 pos = o + d * s + u1 * rideSideOffset;
            pos.y = _observerPos.y + rideHeightOffset;
            return pos;
        }
    }
}
