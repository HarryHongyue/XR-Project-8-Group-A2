using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace Project8.Diagnostics
{
    /// <summary>
    /// Writes a snapshot of the runtime XR state into Logs/vr_diag.txt so VR
    /// problems can be debugged from the file instead of guesswork.
    /// Dumps at 0.5 s, 2 s and 4 s after scene start.
    /// </summary>
    public class VRDiagnostics : MonoBehaviour
    {
        static string Path_ => Path.Combine(Application.dataPath, "..", "Logs", "vr_diag.txt");

        IEnumerator Start()
        {
            yield return new WaitForSeconds(0.5f);
            Dump("t=0.5");
            yield return new WaitForSeconds(1.5f);
            Dump("t=2.0");
            yield return new WaitForSeconds(2f);
            Dump("t=4.0");
        }

        void Dump(string tag)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"--- {tag} ---");
            sb.AppendLine($"scene={SceneManager.GetActiveScene().name}");
            sb.AppendLine($"xrDeviceActive={XRSettings.isDeviceActive} device={XRSettings.loadedDeviceName}");
            try { sb.AppendLine($"stereoMode={XRSettings.stereoRenderingMode} enabled={XRSettings.enabled}"); }
            catch { sb.AppendLine("stereoMode=?"); }

            var cams = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine($"cameras={cams.Length}");
            foreach (var c in cams)
            {
                sb.AppendLine($"  cam '{c.name}' active={c.gameObject.activeInHierarchy} enabled={c.enabled} " +
                              $"pos={c.transform.position:F2} fwd={c.transform.forward:F2} " +
                              $"near={c.nearClipPlane:F3} far={c.farClipPlane} mask={c.cullingMask} " +
                              $"stereoTarget={c.stereoTargetEye} tag={c.tag}");
            }

            int activeMesh = 0, inactive = 0, particles = 0, canvases = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r is ParticleSystemRenderer) { particles++; continue; }
                if (r.gameObject.activeInHierarchy && r.enabled) activeMesh++; else inactive++;
            }
            canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            sb.AppendLine($"renderers: mesh+lines active={activeMesh} inactive={inactive} particles={particles} canvases={canvases}");

            try
            {
                File.AppendAllText(Path_, sb.ToString() + "\n");
            }
            catch { }
        }
    }
}
