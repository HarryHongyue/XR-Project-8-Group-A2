using UnityEngine;

namespace Project8.UI
{
    /// <summary>
    /// Keeps a TextMesh label readable from any viewpoint: TextMesh renders on its
    /// local -Z side, so we point the +Z axis straight away from the camera each frame.
    /// </summary>
    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }

    /// <summary>
    /// A world-space Canvas renders its text on the local -Z side (a default
    /// canvas is read from in FRONT of its negative forward axis), so - like
    /// the TextMesh billboard above - +Z must point AWAY from the camera.
    /// Aiming +Z at the viewer is what produced mirrored text.
    /// </summary>
    public class CanvasBillboard : MonoBehaviour
    {
        public Camera targetCamera;

        void LateUpdate()
        {
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;
            Vector3 away = transform.position - cam.transform.position;
            if (away.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
    }
}
