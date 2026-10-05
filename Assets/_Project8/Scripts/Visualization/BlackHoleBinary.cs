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
