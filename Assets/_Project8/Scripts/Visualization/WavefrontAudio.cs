using UnityEngine;
using Project8.Core;

namespace Project8.Visualization
{
    /// <summary>
    /// Spatial audio cue attached to the wavefront marker: a low hum whose volume tracks
    /// the local |strain| and whose pitch tracks the model frequency. Gives users an
    /// auditory channel for "where the disturbance is" - attention guidance without UI.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class WavefrontAudio : MonoBehaviour
    {
        public GravitationalWaveModel model;
        public SimulationController simulation;
        [Range(0f, 1f)] public float maxVolume = 0.45f;

        AudioSource _source;

        void Awake() => _source = GetComponent<AudioSource>();

        void Update()
        {
            if (model == null || simulation == null || _source == null) return;
            float h = Mathf.Abs(model.StrainAt(transform.position, simulation.SimulationTime));
            _source.volume = maxVolume * Mathf.Clamp01(h * 2f);
            _source.pitch = 0.5f + model.parameters.frequencyHz * 0.5f;
        }
    }
}
