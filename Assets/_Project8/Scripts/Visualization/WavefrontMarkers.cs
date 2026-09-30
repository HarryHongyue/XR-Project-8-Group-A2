using UnityEngine;
using Project8.Core;

namespace Project8.Visualization
{
    /// <summary>
    /// Positions the wavefront guide markers. The centre marker tracks where the disturbance
    /// has reached (s = v*t along the propagation axis); the "ahead" and "behind" markers
    /// bracket it so users can reason about which test objects will be affected next.
    /// Markers hide once the front has passed the field.
    /// </summary>
    public class WavefrontMarkers : MonoBehaviour
    {
        public GravitationalWaveModel model;
        public SimulationController simulation;

        [Header("Marker transforms (ring frames perpendicular to propagation)")]
        public Transform wavefrontMarker;
        public Transform aheadMarker;
        public Transform behindMarker;

        [Tooltip("Distance in metres by which the guide markers lead/lag the wavefront.")]
        [Min(0.05f)] public float markerOffset = 0.75f;

        [Tooltip("Beyond this distance from the origin the markers are hidden.")]
        public float maxVisibleDistance = 8f;

        Quaternion _markerRotation = Quaternion.identity;

        void Update()
        {
            if (model == null || simulation == null) return;

            float s = model.WavefrontDistance(simulation.SimulationTime);
            Vector3 d = model.PropagationDirection;
            Vector3 o = model.Origin;
            model.GetTransverseAxes(out _, out Vector3 up);
            var rot = Quaternion.LookRotation(d, up);
            _markerRotation = rot;

            Place(wavefrontMarker, o + d * s, s);
            Place(aheadMarker, o + d * (s + markerOffset), s + markerOffset);
            Place(behindMarker, o + d * (s - markerOffset), s - markerOffset);
        }

        void Place(Transform marker, Vector3 position, float s)
        {
            if (marker == null) return;
            bool visible = s >= 0f && s <= maxVisibleDistance;
            if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
            marker.SetPositionAndRotation(position, _markerRotation);
        }
    }
}
