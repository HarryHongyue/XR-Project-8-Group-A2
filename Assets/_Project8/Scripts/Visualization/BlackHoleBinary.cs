using UnityEngine;
using UnityEngine.Events;
using Project8.Core;

namespace Project8.Visualization
{
    /// <summary>
    /// Drives the binary black-hole visual: two "holes" (black sphere + accretion disc)
    /// orbit a common centre, spiral inward as their orbital speed rises, merge with a
    /// flash, and leave a single heavier remnant. Timing is tied to SimulationTime so the
    /// merger peak coincides with the Chirp waveform's peak (the wave then takes
    /// distance/visualizationSpeed seconds to reach the test-mass net).
    /// </summary>
    public class BlackHoleBinary : MonoBehaviour
    {
        public SimulationController simulation;

        [Header("Actors")]
        public Transform blackHoleA;
        public Transform blackHoleB;
        [Tooltip("Remnant hole, hidden until merger.")]
        public Transform mergedHole;
        public Light mergerFlash;
        [Tooltip("Disc renderers per hole, used for the merger scale-pop.")]
        public Transform discA;
        public Transform discB;

        [Header("Orbit")]
        [Min(1f)] public float startSeparation = 10f;
        [Tooltip("Matches SimulationParameters.inspiralDurationSeconds so flash and chirp peak coincide.")]
        [Min(0.5f)] public float inspiralDuration = 8f;
        public float baseSpinDegPerSec = 30f;
        [Tooltip("Orbital speed multiplier near merger - Kepler-style speed-up.")]
        public float spinRamp = 12f;
        [Tooltip("Slight orbital-plane tilt for parallax, in degrees.")]
        public float orbitTiltDeg = 18f;

        public UnityEvent onMerger = new UnityEvent();
        public bool HasMerged { get; private set; }

        float _angleDeg;
        Vector3 _com;
        Quaternion _tilt;

        void Start()
        {
            _com = transform.position;
            _tilt = Quaternion.Euler(orbitTiltDeg, 0f, 0f);
            if (mergedHole != null) mergedHole.gameObject.SetActive(false);
            if (simulation != null) simulation.onSimulationReset.AddListener(SyncToSimulationTime);
        }

        void OnDestroy()
        {
            if (simulation != null) simulation.onSimulationReset.RemoveListener(SyncToSimulationTime);
        }

        /// <summary>Replay/reset support: restore the two holes so the merger can re-run.</summary>
        void ResetBinary()
        {
            HasMerged = false;
            _angleDeg = 0f;
            if (blackHoleA != null) blackHoleA.gameObject.SetActive(true);
            if (blackHoleB != null) blackHoleB.gameObject.SetActive(true);
            if (mergedHole != null) mergedHole.gameObject.SetActive(false);
            if (mergerFlash != null) mergerFlash.intensity = 0f;
        }

        /// <summary>Seek-safe resync after RestartAt: the binary pose is recomputed
        /// deterministically from the CURRENT simulation time. Seeking to t less
        /// than the inspiral places the pair at their analytic separation/angle;
        /// seeking at or past it shows the merged remnant immediately - without a
        /// second flash or another fake merger event.</summary>
        public void SyncToSimulationTime()
        {
            if (simulation == null) { ResetBinary(); return; }
            float t = simulation.SimulationTime;
            float td = Mathf.Max(inspiralDuration, 1e-3f);
            if (t >= td)
            {
                HasMerged = true;
                if (blackHoleA != null) blackHoleA.gameObject.SetActive(false);
                if (blackHoleB != null) blackHoleB.gameObject.SetActive(false);
                if (mergedHole != null)
                {
                    mergedHole.position = _com;
                    mergedHole.gameObject.SetActive(true);
                }
                if (mergerFlash != null) mergerFlash.intensity = 0f;
                return;
            }
            HasMerged = false;
            float x = Mathf.Clamp01(t / td);
            // Analytic integral of the Update() spin profile: angle(t) =
            // baseSpin * (t + spinRamp * t^4 / (4 td^3)).
            _angleDeg = baseSpinDegPerSec *
                (t + spinRamp * t * t * t * t / (4f * td * td * td));
            float sep = Mathf.Lerp(startSeparation, 0.9f, Mathf.Pow(x, 1.7f));
            var dir = _tilt * (Quaternion.Euler(0f, _angleDeg, 0f) * Vector3.right);
            if (blackHoleA != null)
            {
                blackHoleA.gameObject.SetActive(true);
                blackHoleA.position = _com + dir * (sep * 0.5f);
            }
            if (blackHoleB != null)
            {
                blackHoleB.gameObject.SetActive(true);
                blackHoleB.position = _com - dir * (sep * 0.5f);
            }
            if (mergedHole != null) mergedHole.gameObject.SetActive(false);
            if (mergerFlash != null) mergerFlash.intensity = 0f;
        }

        void Update()
        {
            if (simulation == null) return;
            float t = simulation.SimulationTime;
            float dt = simulation.IsPlaying ? Time.deltaTime * simulation.TimeScale : 0f;

            float x = Mathf.Clamp01(t / Mathf.Max(inspiralDuration, 1e-3f));

            if (!HasMerged)
            {
                if (x >= 1f)
                {
                    Merge();
                    return;
                }

                // separation shrinks nonlinearly - the plunge accelerates inward
                float sep = Mathf.Lerp(startSeparation, 0.9f, Mathf.Pow(x, 1.7f));
                _angleDeg += dt * baseSpinDegPerSec * (1f + spinRamp * x * x * x);

                var dir = _tilt * (Quaternion.Euler(0f, _angleDeg, 0f) * Vector3.right);
                if (blackHoleA != null) blackHoleA.position = _com + dir * (sep * 0.5f);
                if (blackHoleB != null) blackHoleB.position = _com - dir * (sep * 0.5f);

                // accretion discs whirl ever faster
                float discSpin = dt * (140f + 900f * x);
                if (discA != null) discA.Rotate(0f, discSpin, 0f, Space.Self);
                if (discB != null) discB.Rotate(0f, discSpin, 0f, Space.Self);
            }
            else if (mergerFlash != null && mergerFlash.intensity > 0f)
            {
                mergerFlash.intensity = Mathf.MoveTowards(mergerFlash.intensity, 0f, dt * 2.5f);
                if (mergedHole != null) mergedHole.Rotate(0f, dt * 30f, 0f, Space.World);
            }
        }

        void Merge()
        {
            HasMerged = true;
            if (blackHoleA != null) blackHoleA.gameObject.SetActive(false);
            if (blackHoleB != null) blackHoleB.gameObject.SetActive(false);
            if (mergedHole != null)
            {
                mergedHole.position = _com;
                mergedHole.gameObject.SetActive(true);
            }
            if (mergerFlash != null) mergerFlash.intensity = mergerFlash.intensity <= 0f ? 6f : mergerFlash.intensity * 6f;
            onMerger.Invoke();
        }
    }
}
