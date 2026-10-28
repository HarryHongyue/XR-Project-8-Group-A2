using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project8.EditorTools
{
    /// <summary>
    /// Workspace-driven build+verify runner. Lets an external tool trigger the
    /// scene rebuild and the physics/visual regression INSIDE the already-running
    /// editor - no second Unity instance, no project lock conflict.
    ///
    /// Protocol:
    ///   1. A tool drops <project>/.tools/et-ui-redesign-request.json.
    ///   2. While the editor is idle (not compiling, not importing, not playing)
    ///      the runner CONSUMES the flag by renaming it (never deleting), then:
    ///        a. file-backs-up everything the builder overwrites into
    ///           .tools/visual-fix-backup-<timestamp>/ preserving relative paths;
    ///        b. snapshot-saves every DIRTY loaded scene as a copy under
    ///           Assets/_Project8/Scenes/__FixBackups/<ts>/ (SaveScene requires a
    ///           path inside Assets - the originals are never modified);
    ///        c. asks the user to save modified scenes (Cancel aborts the run);
    ///        d. SceneBuilder.BuildAll() - failures propagate out of BuildAll;
    ///        e. PhysicsVisualRegression.Run() - throws on any failed check;
    ///        f. reopens Main.unity as the active scene (never Welcome/empty);
    ///        g. writes Logs/visual_fix_result.json for the caller.
    ///
    /// All reads/writes stay inside the project. Nothing is deleted; the flag is
    /// renamed to .consumed-<ts> and backups are kept permanently.
    /// </summary>
    public static class VisualFixRunner
    {
        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        static string RequestPath => Path.Combine(ProjectRoot, ".tools", "et-ui-redesign-request.json");
        /// <summary>Bumped whenever the verified code revision changes, so a
        /// fresh result cannot be confused with an older run.</summary>
        const string ResultRevision = "et-ui-redesign-2";
        static double _nextPoll;

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 1.5;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(RequestPath)) return;

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string consumed = RequestPath + ".consumed-" + stamp;
            try { File.Move(RequestPath, consumed); }
            catch (Exception e)
            {
                Debug.LogError($"[Project8] VisualFixRunner could not consume flag: {e.Message}");
                return;
            }
            Execute(consumed, stamp);
        }

        static void Execute(string consumedFlag, string stamp)
        {
            var log = new List<string>();
            void Note(string msg) { log.Add(msg); Debug.Log($"[Project8] {msg}"); }
            string backupDir = Path.Combine(ProjectRoot, ".tools",
                "visual-fix-backup-" + stamp);
            string sceneBackupDir = "Assets/_Project8/Scenes/__FixBackups/" + stamp;
            string status = "failure", error = "";

            try
            {
                // --- a. file backups of everything the builder rewrites --------
                var backedUp = BackupFiles(backupDir);
                Note($"file backup: {backedUp} files -> {backupDir}");

                // --- b. snapshot dirty loaded scenes as COPIES (originals kept) -
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (!s.isLoaded || !s.isDirty || string.IsNullOrEmpty(s.path)) continue;
                    string copy = $"{sceneBackupDir}/{Path.GetFileName(s.path)}";
                    Directory.CreateDirectory(Path.GetDirectoryName(copy));
                    if (EditorSceneManager.SaveScene(s, copy, true))
                        Note($"dirty scene snapshot: {s.path} -> {copy}");
                }

                // --- c. never silently discard the user's unsaved scene work ---
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    status = "aborted";
                    error = "user cancelled the scene-save prompt";
                    Note("run aborted before BuildAll - nothing was rebuilt");
                    return;
                }

                // --- d. rebuild scenes (BuildAll rethrows on failure) ----------
                Note("running SceneBuilder.BuildAll ...");
                SceneBuilder.BuildAll();
                Note("BuildAll finished");

                // --- e. deterministic physics/visual regression ---------------
                Note("running PhysicsVisualRegression ...");
                PhysicsVisualRegression.Run();
                Note("regression passed");

                // --- e2. desktop-preview screenshots (never fails the run) ----
                try
                {
                    int shots = CaptureRedesignEvidence();
                    Note($"evidence screenshots: {shots}");
                }
                catch (Exception se)
                {
                    Note("screenshot capture skipped: " + se.Message);
                }

                // --- f. leave the user looking at the fresh Main scene ---------
                EditorSceneManager.OpenScene("Assets/_Project8/Scenes/Main.unity");
                status = "success";
            }
            catch (Exception e)
            {
                status = "failure";
                error = e.Message;
                Debug.LogException(e);
            }
            finally
            {
                WriteResult(consumedFlag, backupDir, status, error, log);
            }
        }

        /// <summary>Copies every file the builder rewrites into the backup tree,
        /// preserving relative paths. Skips .pen scratch files.</summary>
        static int BackupFiles(string backupDir)
        {
            var dirs = new[]
            {
                "Assets/_Project8/Scenes", "Assets/_Project8/Materials",
                "Assets/_Project8/Prefabs", "Assets/_Project8/Settings",
                "Assets/XR", "Assets/XRI"
            };
            var files = new[]
            {
                "ProjectSettings/EditorBuildSettings.asset",
                "ProjectSettings/ProjectSettings.asset",
            };
            int count = 0;
            foreach (string rel in dirs)
            {
                string abs = Path.Combine(ProjectRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(abs)) continue;
                foreach (string f in Directory.GetFiles(abs, "*", SearchOption.AllDirectories))
                {
                    if (f.EndsWith(".pen", StringComparison.OrdinalIgnoreCase)) continue;
                    count += CopyRel(f, backupDir);
                }
            }
            foreach (string rel in files)
            {
                string abs = Path.Combine(ProjectRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(abs)) count += CopyRel(abs, backupDir);
            }
            return count;
        }

        static int CopyRel(string absFile, string backupDir)
        {
            string rel = absFile.Substring(ProjectRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string dest = Path.Combine(backupDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(absFile, dest, true);
            return 1;
        }

        /// <summary>Desktop-preview captures for the redesign (NO VR claim):
        /// seated Controls-drawer shots (Wave Lab tab, then ET tab) plus one
        /// elevated overview of the ET triangle. Writes Logs/et-ui-controls.png,
        /// Logs/et-ui-etpage.png and Logs/et-overview.png; returns the count
        /// produced. Restores panel/tab/selection/visibility state afterwards;
        /// it never clears user dirty marks.</summary>
        [MenuItem("Project 8/Capture Redesign Evidence")]
        public static void CaptureRedesignEvidenceMenu()
        {
            Debug.Log($"[Project8] evidence screenshots: {CaptureRedesignEvidence()}");
        }

        static int CaptureRedesignEvidence()
        {
            string mainPath = "Assets/_Project8/Scenes/Main.unity";
            var mainScene = SceneManager.GetSceneByPath(mainPath);
            bool opened = false;
            if (!mainScene.isLoaded)
            {
                mainScene = EditorSceneManager.OpenScene(mainPath, OpenSceneMode.Additive);
                opened = true;
            }
            GameObject camGO = null;
            int shots = 0;
            var et = FindComponentInScene<Project8.Visualization.EinsteinTelescopeDisplay>(
                mainScene);
            int prevSelection = et != null ? et.SelectedDetector : -1;
            bool prevDetail = et != null && et.DetailVisible;
            try
            {
                var ship = FindInScene(mainScene, "Spaceship");
                var cockpit = ship != null
                    ? ship.GetComponent<Project8.Interaction.CockpitControls>() : null;
                var eyeCam = ship != null
                    ? ship.GetComponentInChildren<Camera>(true) : null;
                Vector3 eyePos = eyeCam != null ? eyeCam.transform.position
                    : (ship != null ? ship.transform.position : Vector3.zero)
                      + new Vector3(0f, 1.25f, 0f);

                camGO = new GameObject("__EvidenceCam");
                var c = camGO.AddComponent<Camera>();
                // Bind the capture camera to the Main scene explicitly so no
                // helper camera from another open scene (e.g. Welcome) renders.
                c.scene = mainScene;
                c.enabled = false;
                c.clearFlags = CameraClearFlags.Skybox;
                c.fieldOfView = 60f;
                c.nearClipPlane = 0.02f;
                c.farClipPlane = 3000f;
                c.stereoTargetEye = StereoTargetEyeMask.None;

                // Shot 1: seated view with the Controls drawer open. Components'
                // Start() never runs in edit mode, so seed every UI refresh by
                // hand before forcing the canvas update + render.
                var controls = cockpit != null ? cockpit.controlPanel : null;
                bool wasActive = controls != null && controls.activeSelf;
                if (controls != null && !wasActive) controls.SetActive(true);
                try
                {
                    var lib = FindComponentInScene<Project8.Evaluation.WaveLibrary>(mainScene);
                    var simUI = controls != null
                        ? controls.GetComponent<Project8.UI.SimulationUI>() : null;
                    if (lib != null) lib.RefreshSlots();
                    if (simUI != null)
                    {
                        simUI.RefreshWidgets();
                        simUI.RefreshInteractables();
                    }
                    var tabs = controls != null
                        ? controls.GetComponent<Project8.UI.ConsoleTabs>() : null;
                    if (tabs != null) tabs.ShowWave();
                    if (et != null)
                    {
                        // Update() never runs in edit mode: initialize each
                        // lane's beams/mirrors/dark port at t=0 so the shots do
                        // not show fabricated pre-Update beam state.
                        if (et.detectors != null)
                            foreach (var lane in et.detectors)
                                if (lane != null) lane.Refresh(0f);
                        et.SelectDetector(1);
                        et.RefreshInspector();
                    }
                    Canvas.ForceUpdateCanvases();
                    c.transform.position = eyePos;
                    Vector3 panelPos = ship != null
                        ? ship.TransformPoint(new Vector3(0.12f, 1.60f, 1.45f))
                        : eyePos + Vector3.forward;
                    c.transform.rotation =
                        Quaternion.LookRotation(panelPos - eyePos, Vector3.up);
                    Shoot(c, "et-ui-controls.png");
                    shots++;

                    // Shot 1b: same seated view, ET tab active.
                    if (tabs != null)
                    {
                        tabs.ShowET();
                        Canvas.ForceUpdateCanvases();
                        Shoot(c, "et-ui-etpage.png");
                        shots++;
                        tabs.ShowWave();
                    }
                }
                finally
                {
                    if (controls != null && !wasActive) controls.SetActive(false);
                }

                // Shot 2: elevated oblique overview of the ET triangle.
                if (et != null)
                {
                    c.transform.position = et.transform.position + new Vector3(0f, 16f, -15f);
                    c.transform.LookAt(et.transform.position + Vector3.up * 0.5f);
                    Shoot(c, "et-overview.png");
                    shots++;
                }
            }
            finally
            {
                // Restore the ET selection/visibility we touched and drop the
                // capture camera - user dirty marks are left exactly as found.
                if (et != null && prevSelection >= 0)
                {
                    et.SelectDetector(prevSelection);
                    et.SetDetailVisible(prevDetail);
                }
                if (camGO != null) UnityEngine.Object.DestroyImmediate(camGO);
                if (opened && mainScene.isLoaded)
                    EditorSceneManager.CloseScene(mainScene, true);
            }
            return shots;
        }

        static void Shoot(Camera c, string file)
        {
            var rt = new RenderTexture(1280, 720, 24);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var prevActive = RenderTexture.active;
            try
            {
                c.targetTexture = rt;
                c.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
                tex.Apply();
                string logsDir = Path.Combine(ProjectRoot, "Logs");
                Directory.CreateDirectory(logsDir);
                File.WriteAllBytes(Path.Combine(logsDir, file), tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = prevActive;
                c.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        static Transform FindInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Transform>(true))
                    if (c.name == name) return c;
            return null;
        }

        static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var c = root.GetComponentInChildren<T>(true);
                if (c != null) return c;
            }
            return null;
        }

        static void WriteResult(string consumedFlag, string backupDir,
                                string status, string error, List<string> log)
        {
            try
            {
                string logsDir = Path.Combine(ProjectRoot, "Logs");
                Directory.CreateDirectory(logsDir);
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append($"  \"status\": \"{status}\",\n");
                sb.Append($"  \"revision\": \"{ResultRevision}\",\n");
                sb.Append($"  \"finishedUtc\": \"{DateTime.UtcNow:o}\",\n");
                sb.Append($"  \"consumedFlag\": \"{Json(consumedFlag)}\",\n");
                sb.Append($"  \"backupDir\": \"{Json(backupDir)}\",\n");
                sb.Append($"  \"error\": \"{Json(error)}\",\n");
                sb.Append("  \"steps\": [\n");
                for (int i = 0; i < log.Count; i++)
                    sb.Append($"    \"{Json(log[i])}\"{(i < log.Count - 1 ? "," : "")}\n");
                sb.Append("  ]\n}\n");
                File.WriteAllText(Path.Combine(logsDir, "visual_fix_result.json"),
                    sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError($"[Project8] VisualFixRunner result write failed: {e.Message}");
            }
        }

        static string Json(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
                     .Replace("\n", "\\n").Replace("\r", "");
    }
}
