using UnityEngine;
using Project8.Core;

namespace Project8.Interaction
{
    /// <summary>
    /// Maps the vertical position of a grabbable "strain handle" to the strain amplitude:
    /// lift the orb to stretch more, lower it to flatten the wave. Embodies the Track-A
    /// principle that abstract data should be physically manipulable.
    /// Works with any grab system (XRI sets the transform; this component only reads it).
    /// </summary>
    public class GrabToParameter : MonoBehaviour
    {
        public SimulationParameters parameters;
        [Tooltip("Neutral height of the handle. Dragging above/below it scales amplitude.")]
        public Transform restAnchor;
        [Tooltip("Amplitude change per metre of vertical displacement.")]
        public float metresToAmplitude = 1.5f;

        float _base;

        void OnEnable() => _base = parameters != null ? parameters.strainAmplitude : 0.35f;

        void Update()
        {
            if (parameters == null || restAnchor == null) return;
            float dy = transform.position.y - restAnchor.position.y;
            // Only drive the parameter while the handle is actually displaced.
            // Otherwise an idle handle would keep snapping strainAmplitude back to
            // the value captured at OnEnable - which is what made the amplitude
            // slider spring back whenever the user dragged it.
            if (Mathf.Abs(dy) < 0.005f)
            {
                _base = parameters.strainAmplitude;
                return;
            }
            float value = Mathf.Clamp01(_base + dy * metresToAmplitude);
            if (!Mathf.Approximately(value, parameters.strainAmplitude))
            {
                parameters.strainAmplitude = value;
                parameters.NotifyChanged();
            }
        }

        /// <summary>Snap the handle back to its neutral height.</summary>
        public void ResetHandle()
        {
            if (restAnchor != null) transform.position = restAnchor.position;
        }
    }
}
