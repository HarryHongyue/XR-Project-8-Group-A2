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
}
