using UnityEngine;
using Project8.Core;

namespace Project8.Visualization
{
    /// <summary>
    /// Applies the passing wave's '+'-polarization deformation to Earth (or any body):
    /// squashes along one transverse axis while stretching the other. visualGain makes
    /// the deformation visible on a planet-scale object; combined with the parameters'
    /// displacementExaggeration this is the labelled exaggeration the storyboard wants.
    /// </summary>
    public class EarthDeformer : MonoBehaviour
    {
        public GravitationalWaveModel model;
        public SimulationController simulation;
        [Tooltip("Extra gain so a planet-sized body visibly deforms. Still an exaggeration label.")]
        public float visualGain = 0.35f;
        public float maxDeform = 0.25f;

        Vector3 _restScale;

        void Start() => _restScale = transform.localScale;

        void Update()
        {
            if (model == null || simulation == null) return;
            float h = model.StrainAt(transform.position, simulation.SimulationTime);
            float ex = model.parameters != null ? model.parameters.displacementExaggeration : 1f;
            float k = Mathf.Clamp(h * ex * visualGain, -maxDeform, maxDeform);
            transform.localScale = new Vector3(
                _restScale.x * (1f + k), _restScale.y * (1f - k), _restScale.z);
        }
    }
}
