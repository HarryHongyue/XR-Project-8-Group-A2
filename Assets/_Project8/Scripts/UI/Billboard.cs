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
    /// Canvas labels render on the +Z-facing side (unlike TextMesh). Keep a
    /// world-space label oriented toward the camera so its text is not mirrored
    /// or hidden when the viewer moves around the optical bench.
    /// </summary>
    public class CanvasBillboard : MonoBehaviour
    {
        public Camera targetCamera;

        void LateUpdate()
        {
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;
            Vector3 toCamera = cam.transform.position - transform.position;
            if (toCamera.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        }
    }
}
