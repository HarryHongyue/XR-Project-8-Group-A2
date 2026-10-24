using UnityEngine;
using Project8.Core;

namespace Project8.Visualization
{
    /// <summary>
    /// Applies the passing wave's '+'-polarization deformation to Earth (or any body).
    ///
    /// The small-strain tensor is F = I + k (u1 u1^T - u2 u2^T) where u1/u2 are the
    /// transverse '+' axes at the body's position. A scale-only transform cannot
    /// represent the full tensor, so we apply its diagonal in Earth-local axes:
    /// each local axis e scales by 1 + k((e.u1)^2 - (e.u2)^2). For a wave arriving
    /// along +X this deforms the sphere in local Y/Z - the physically transverse
    /// directions - instead of the old X/Y scaling that stretched along the
    /// propagation axis itself.
    ///
    /// visualGain makes the deformation visible on a planet-scale object; combined
    /// with the parameters' displacementExaggeration this is the labelled
    /// exaggeration the storyboard wants.
    /// </summary>
    public class EarthDeformer : MonoBehaviour
    {
        public GravitationalWaveModel model;
        public SimulationController simulation;
        [Tooltip("Extra gain so a planet-sized body visibly deforms. Still an exaggeration label.")]
        public float visualGain = 0.35f;
        public float maxDeform = 0.25f;

        Vector3 _restScale;
        Vector3 _u1, _u2;
        float _k;

        void Start() => _restScale = transform.localScale;

        void Update()
        {
            if (model == null || simulation == null) return;
            float h = model.StrainAt(transform.position, simulation.SimulationTime);
            float ex = model.parameters != null ? model.parameters.displacementExaggeration : 1f;
            _k = h * ex * visualGain;
            model.GetTransverseAxes(transform.position, out _u1, out _u2);
            transform.localScale = new Vector3(
                _restScale.x * (1f + AxisFactor(Vector3.right)),
                _restScale.y * (1f + AxisFactor(Vector3.up)),
                _restScale.z * (1f + AxisFactor(Vector3.forward)));
        }

        /// <summary>Diagonal entry of the strain tensor along one Earth-local axis.</summary>
        float AxisFactor(Vector3 localAxis)
        {
            Vector3 a = transform.TransformDirection(localAxis).normalized;
            float d1 = Vector3.Dot(a, _u1);
            float d2 = Vector3.Dot(a, _u2);
            return Mathf.Clamp(_k * (d1 * d1 - d2 * d2), -maxDeform, maxDeform);
        }
    }
}
