using UnityEngine;
using Project8.Core;

namespace Project8.Visualization
{
    /// <summary>
    /// Miniature LIGO-style interferometer display: two perpendicular laser arms whose
    /// drawn length follows the '+'-polarization strain (one stretches while the other
    /// squeezes), and whose brightness pulses with |h|. Links the telescope/AI explanation
    /// to how real detectors measure gravitational waves.
    /// </summary>
    public class LaserInterferometer : MonoBehaviour
    {
        public GravitationalWaveModel model;
        public SimulationController simulation;
        public LineRenderer armA;   // stretches with +h
        public LineRenderer armB;   // squeezes with +h
        [Tooltip("Visible length change per unit strain (exaggerated, like everything else).")]
        public float armGain = 0.8f;
        public float minWidth = 0.008f;
        public float maxWidth = 0.05f;

        Vector3 _endA;
        Vector3 _endB;

        void Start()
        {
            if (armA != null) _endA = armA.GetPosition(1);
            if (armB != null) _endB = armB.GetPosition(1);
        }

        void Update()
        {
            if (model == null || simulation == null) return;
            float h = model.StrainAt(transform.position, simulation.SimulationTime);
            float ex = model.parameters != null ? model.parameters.displacementExaggeration : 1f;
            float k = h * ex * armGain;

            if (armA != null) armA.SetPosition(1, _endA * (1f + k));
            if (armB != null) armB.SetPosition(1, _endB * (1f - k));

            float w = Mathf.Lerp(minWidth, maxWidth, Mathf.Clamp01(Mathf.Abs(h) * 2f));
            if (armA != null) armA.widthMultiplier = w;
            if (armB != null) armB.widthMultiplier = w;
        }
    }
}
