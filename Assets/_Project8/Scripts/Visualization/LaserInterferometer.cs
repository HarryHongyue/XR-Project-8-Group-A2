using UnityEngine;
using Project8.Core;

namespace Project8.Visualization
{
    /// <summary>
    /// LIGO-style Michelson detector - a teaching cutaway, not a full Einstein
    /// Telescope model. Laser light enters along local -X, is split at the origin
    /// into two perpendicular arms (local +X and +Y), returns off the end mirrors
    /// and recombines at the splitter - the differential output goes to the
    /// photodetector at local -Y (the "dark port").
    ///
    /// Physics content: a passing wave changes each arm's length by
    /// deltaL/L = 0.5 * h * ex * ((a.u1)^2 - (a.u2)^2), the standard transverse
    /// strain projection onto the arm axis a. One arm stretches while the other
    /// squeezes, the round trips fall out of step, and the dark port brightens
    /// with I = sin(phase/2)^2.
    ///
    /// Labelled simplifications: arms are a few metres (not kilometres), there are
    /// no Fabry-Perot cavities or suspensions, mirror motion is exaggerated, the
    /// markers' light crawl and the "display wavelength" are slowed/scaled
    /// pedagogical quantities - not real laser light.
    /// </summary>
    public class LaserInterferometer : MonoBehaviour
    {
        public GravitationalWaveModel model;
        public SimulationController simulation;

        [Header("Geometry")]
        [Tooltip("Local-space axis of arm A (normalized on use). Default +X keeps the " +
                 "LIGO-style upright pair; the ET display sets 60-degree XZ axes.")]
        public Vector3 armAxisA = Vector3.right;
        [Tooltip("Local-space axis of arm B (normalized on use).")]
        public Vector3 armAxisB = Vector3.up;
        [Tooltip("Rest length of each arm in local metres (arms shortened vs. the kilometres of a real detector).")]
        public float armLength = 5f;
        [Tooltip("Cap on the displayed fractional arm change - keeps the exaggerated motion readable.")]
        public float maxArmFraction = 0.18f;
        [Tooltip("Teaching 'wavelength' for the interference phase, in local units. " +
                 "NOT a real laser wavelength; a real detector's phase gain also comes from its arm cavities.")]
        public float displayWavelength = 4f;
        [Tooltip("Visible crawl speed of the light markers along an arm (slowed for teaching).")]
        public float markerSpeed = 1.6f;
        [Tooltip("Lateral separation of the outbound/return beam lines (readability only).")]
        public float beamOffset = 0.07f;

        [Header("Actors")]
        public Transform mirrorA;   // end mirror of arm A (local +X)
        public Transform mirrorB;   // end mirror of arm B (local +Y)
        public Transform markerA;   // light marker riding arm A, out and back
        public Transform markerB;   // light marker riding arm B
        public Renderer photodetector;
        public TextMesh readout;

        [Header("Beams (local space, fixed width)")]
        public LineRenderer inputBeam;
        public LineRenderer outboundA;
        public LineRenderer inboundA;
        public LineRenderer outboundB;
        public LineRenderer inboundB;
        public LineRenderer outputBeam;
        public LineRenderer signalTrace;

        public float ArmFractionA { get; private set; }
        public float ArmFractionB { get; private set; }
        public float CurrentPhase { get; private set; }
        public float DarkPortIntensity { get; private set; }

        const int TracePoints = 48;
        const float TraceSamplesPerSecond = 8f;
        int _lastTraceSample = -1;
        float _lastReadoutIntensity = -1f;
        MaterialPropertyBlock _mpb;

        // At zero intensity the dark port must be truly dark: fully transparent.
        static readonly Color BeamDim = new Color(0f, 0f, 0f, 0f);
        static readonly Color BeamLit = new Color(0.5f, 1f, 1f, 0.95f);
        static readonly Color DetectorDim = new Color(0.02f, 0.02f, 0.05f, 1f);
        static readonly Color DetectorLit = new Color(0.4f, 1f, 1f, 1f);

        void Awake() => _mpb = new MaterialPropertyBlock();

        void Update()
        {
            if (model == null || simulation == null) return;
            Refresh(simulation.SimulationTime);
        }

        /// <summary>Deterministic recomputation from simulation time only - pause
        /// and reset reproduce identical arm lengths, markers and dark-port light.</summary>
        public void Refresh(float simTime)
        {
            Vector3 pos = transform.position;
            // Local arm axes (normalized once), then world axes for the strain probe.
            Vector3 axA = armAxisA.sqrMagnitude > 1e-6f ? armAxisA.normalized : Vector3.right;
            Vector3 axB = armAxisB.sqrMagnitude > 1e-6f ? armAxisB.normalized : Vector3.up;
            Vector3 axisA = transform.TransformDirection(axA).normalized;
            Vector3 axisB = transform.TransformDirection(axB).normalized;
            ArmFractionA = EvaluateArmFraction(axisA, pos, simTime);
            ArmFractionB = EvaluateArmFraction(axisB, pos, simTime);
            float lenA = armLength * (1f + ArmFractionA);
            float lenB = armLength * (1f + ArmFractionB);
            Vector3 endA = axA * lenA;
            Vector3 endB = axB * lenB;

            // Mirrors ride their own arm axis; beam endpoints track them exactly.
            if (mirrorA != null) mirrorA.localPosition = endA;
            if (mirrorB != null) mirrorB.localPosition = endB;
            // Outbound/return lines separate IN the arm plane, perpendicular to each
            // own axis - readable from above on a horizontal station.
            Vector3 planeN = Vector3.Cross(axA, axB);
            planeN = planeN.sqrMagnitude > 1e-6f ? planeN.normalized : Vector3.forward;
            Vector3 offA = Vector3.Cross(planeN, axA) * beamOffset;
            Vector3 offB = Vector3.Cross(planeN, axB) * beamOffset;
            SetBeam(outboundA, offA, endA + offA);
            SetBeam(inboundA, endA - offA, -offA);
            SetBeam(outboundB, offB, endB + offB);
            SetBeam(inboundB, endB - offB, -offB);

            MoveMarker(markerA, axA, lenA, simTime);
            MoveMarker(markerB, axB, lenB, simTime);

            // Pedagogical interference phase from the round-trip difference.
            float phase = 2f * Mathf.PI * (2f * lenA - 2f * lenB) / displayWavelength;
            CurrentPhase = phase;
            DarkPortIntensity = ComputeDarkPortIntensity(phase);

            ApplyBrightness(DarkPortIntensity);
            if (readout != null &&
                Mathf.Abs(DarkPortIntensity - _lastReadoutIntensity) > 0.005f)
            {
                _lastReadoutIntensity = DarkPortIntensity;
                readout.text = $"phase {phase:0.00} rad\nsignal {DarkPortIntensity:0.00}";
            }
            UpdateTrace(simTime);
        }

        /// <summary>Fractional length change deltaL/L of an arm along worldAxis,
        /// from the '+'-polarization strain tensor evaluated at worldPos.</summary>
        public float EvaluateArmFraction(Vector3 worldAxis, Vector3 worldPos, float simTime)
        {
            if (model == null || model.parameters == null) return 0f;
            float h = model.StrainAt(worldPos, simTime);
            float ex = model.parameters.displacementExaggeration;
            model.GetTransverseAxes(worldPos, out Vector3 u1, out Vector3 u2);
            Vector3 a = worldAxis.normalized;
            float d1 = Vector3.Dot(a, u1);
            float d2 = Vector3.Dot(a, u2);
            return Mathf.Clamp(0.5f * h * ex * (d1 * d1 - d2 * d2),
                -maxArmFraction, maxArmFraction);
        }

        /// <summary>Michelson dark-port intensity: I = sin(phase/2)^2. Equal arms
        /// give zero (dark); a differential arm change lifts light out of the port.</summary>
        public static float ComputeDarkPortIntensity(float phase)
        {
            float s = Mathf.Sin(phase * 0.5f);
            return s * s;
        }

        /// <summary>Visible light packet crawling out and back along an arm,
        /// driven purely by simulation time so pause/reset stay deterministic.</summary>
        void MoveMarker(Transform marker, Vector3 axis, float len, float t)
        {
            if (marker == null) return;
            float arm = Mathf.Max(len, 0.01f);
            float trip = 2f * arm;
            float d = Mathf.Repeat(t * markerSpeed, trip);
            marker.localPosition = axis * (d <= arm ? d : trip - d);
        }

        static void SetBeam(LineRenderer lr, Vector3 a, Vector3 b)
        {
            if (lr == null) return;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
        }

        void ApplyBrightness(float intensity)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            if (photodetector != null)
            {
                photodetector.GetPropertyBlock(_mpb);
                Color c = Color.Lerp(DetectorDim, DetectorLit, intensity);
                _mpb.SetColor("_Color", c);
                _mpb.SetColor("_EmissionColor", c * intensity);
                photodetector.SetPropertyBlock(_mpb);
            }
            if (outputBeam != null)
            {
                outputBeam.GetPropertyBlock(_mpb);
                _mpb.SetColor("_TintColor", Color.Lerp(BeamDim, BeamLit, intensity));
                outputBeam.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>Rolling trace of the last TracePoints/8 seconds of dark-port
        /// intensity. The history is recomputed analytically from simulation time
        /// (arm fractions -> phase -> intensity), so it is deterministic under
        /// pause, replay and reset - rewinding to t = 0 flattens it, and jumping
        /// straight to a time gives the same curve as stepping there.
        /// Only rewritten when the 1/8 s sample index changes.</summary>
        void UpdateTrace(float t)
        {
            if (signalTrace == null) return;
            int sample = (int)Mathf.Floor(t * TraceSamplesPerSecond);
            if (sample == _lastTraceSample) return;
            _lastTraceSample = sample;
            if (signalTrace.positionCount != TracePoints) signalTrace.positionCount = TracePoints;
            Vector3 pos = transform.position;
            Vector3 axA = armAxisA.sqrMagnitude > 1e-6f ? armAxisA.normalized : Vector3.right;
            Vector3 axB = armAxisB.sqrMagnitude > 1e-6f ? armAxisB.normalized : Vector3.up;
            Vector3 axisA = transform.TransformDirection(axA).normalized;
            Vector3 axisB = transform.TransformDirection(axB).normalized;
            for (int i = 0; i < TracePoints; i++)
            {
                float ti = t - (TracePoints - 1 - i) / TraceSamplesPerSecond;
                float intensity = 0f;
                if (ti > 0f)
                {
                    float fA = EvaluateArmFraction(axisA, pos, ti);
                    float fB = EvaluateArmFraction(axisB, pos, ti);
                    float rtA = 2f * armLength * (1f + fA);
                    float rtB = 2f * armLength * (1f + fB);
                    intensity = ComputeDarkPortIntensity(
                        2f * Mathf.PI * (rtA - rtB) / displayWavelength);
                }
                signalTrace.SetPosition(i,
                    new Vector3((i - TracePoints * 0.5f) * 0.09f,
                        -2.1f + intensity * 0.45f, 0f));
            }
        }
    }
}
