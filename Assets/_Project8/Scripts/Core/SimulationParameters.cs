using UnityEngine;

namespace Project8.Core
{
    public enum WaveMode { Continuous, Burst, Chirp }
    public enum PropagationAxis { PositiveX, NegativeX, PositiveY, NegativeY, PositiveZ, NegativeZ }

    /// <summary>
    /// Data-driven parameters for the simplified gravitational-wave teaching model.
    /// Physical parameters (strain amplitude, frequency, phase, propagation axis) are kept
    /// strictly separate from visualization choices (display speed, exaggeration) so that
    /// the UI and documentation can label them honestly.
    /// </summary>
    [CreateAssetMenu(fileName = "SimulationParameters", menuName = "Project 8/Simulation Parameters")]
    public class SimulationParameters : ScriptableObject
    {
        [Header("Physical (model) parameters")]
        [Tooltip("Strain amplitude h0, dimensionless. Real GW strain on Earth is ~1e-21; " +
                 "this displayed value is deliberately exaggerated so the effect is visible.")]
        [Range(0f, 1f)] public float strainAmplitude = 0.35f;

        [Tooltip("Frequency f of the pedagogical sinusoid h(t) = h0 sin(2*pi*f*t + phi), in Hz.")]
        [Range(0.05f, 4f)] public float frequencyHz = 0.5f;

        [Tooltip("Initial phase phi of the sinusoid, in radians.")]
        [Range(0f, Mathf.PI * 2f)] public float phaseRadians = 0f;

        [Tooltip("Axis the wavefront travels along through the test-object field.")]
        public PropagationAxis propagationAxis = PropagationAxis.PositiveZ;

        [Header("Wave mode")]
        public WaveMode waveMode = WaveMode.Burst;

        [Tooltip("Length of the Gaussian wave packet in Burst mode, in seconds of simulation time.")]
        [Min(0.5f)] public float packetDurationSeconds = 4f;

        [Tooltip("Chirp mode: seconds of inspiral before the merger peak. In this mode " +
                 "frequencyHz acts as the peak frequency; the sweep starts at 0.25x it.")]
        [Min(0.5f)] public float inspiralDurationSeconds = 8f;

        [Tooltip("Chirp mode: exponential ringdown decay time after the merger peak, in seconds.")]
        [Min(0.2f)] public float ringdownSeconds = 3f;

        [Tooltip("Chirp mode: fraction of peak amplitude that keeps oscillating after the " +
                 "ringdown decays. STYLIZED - real ringdowns fade to zero; this residual " +
                 "keeps the waves visibly moving so the scene keeps demonstrating strain.")]
        [Range(0f, 0.5f)] public float postMergerResidual = 0.22f;

        [Header("Visualization choices (explicitly NOT physical)")]
        [Tooltip("Displayed propagation speed in m/s. Real gravitational waves travel at the speed " +
                 "of light c; this value only exists so a human can watch the wavefront move.")]
        [Min(0.05f)] public float visualizationSpeedMps = 1.5f;

        [Tooltip("Extra multiplier applied to the transverse displacement for visibility.")]
        [Range(0.25f, 4f)] public float displacementExaggeration = 1f;

        /// <summary>Raised whenever a parameter changes so views can refresh.</summary>
        public event System.Action Changed;
        public void NotifyChanged() => Changed?.Invoke();

        void OnValidate() => Changed?.Invoke();

        public Vector3 PropagationDirection
        {
            get
            {
                switch (propagationAxis)
                {
                    case PropagationAxis.PositiveX: return Vector3.right;
                    case PropagationAxis.NegativeX: return Vector3.left;
                    case PropagationAxis.PositiveY: return Vector3.up;
                    case PropagationAxis.NegativeY: return Vector3.down;
                    case PropagationAxis.NegativeZ: return Vector3.back;
                    default: return Vector3.forward;
                }
            }
        }
    }
}
