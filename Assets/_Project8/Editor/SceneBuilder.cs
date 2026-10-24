using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Project8.AI;
using Project8.Core;
using Project8.Evaluation;
using Project8.Interaction;
using Project8.Perspective;
using Project8.Story;
using Project8.UI;
using Project8.Visualization;

namespace Project8.EditorTools
{
    /// <summary>
    /// Generates the complete demo scene from code so the project is reproducible:
    /// materials, prefabs, parameter assets, the XR rig (from the XRI sample prefab),
    /// the test-object field, wavefront markers, the world-space control panel and the
    /// perspective/prediction/AI-tutor systems. Run via
    ///   Unity.exe -batchmode -projectPath <p> -quit
    ///   -executeMethod Project8.EditorTools.SceneBuilder.BuildAll
    /// or manually via the menu item "Project 8/Build Demo Scene".
    /// </summary>
    public static partial class SceneBuilder
    {
        const string Root = "Assets/_Project8";

        static Font _font;
        static Sprite _uiSprite;
        static Sprite _knobSprite;

        // ---- liquid-glass UI palette -------------------------------------------
        static readonly Color Accent      = new Color(0.50f, 0.90f, 1f);

        [MenuItem("Project 8/Build Demo Scene")]
        public static void BuildAll()
        {
            // The build replaces all open scenes via NewScene(Single) - never
            // silently discard the user's unsaved scene work. In batch mode the
            // prompt auto-saves; Cancel aborts the whole build before any
            // NewScene runs. (No duplicate prompt: nothing dirty -> no dialog.)
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[Project8] Build aborted: scene save cancelled.");
                return;
            }
            try
            {
                LoadBuiltins();
                EnsureFolders();
                var mats = CreateMaterials();
                var prefab = CreateTestMassPrefab(mats.TestMass);
                var storyParams = CreateParameters("SimulationParameters", WaveMode.Chirp);
                var knowledge = CreateKnowledgeBase();
                var hum = CreateHumClip();
                BuildScene(mats, prefab, storyParams, knowledge, hum, "Main");
                BuildWelcomeScene(mats, hum);
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene($"{Root}/Scenes/Welcome.unity", true),
                    new EditorBuildSettingsScene($"{Root}/Scenes/Main.unity", true),
                };
                ConfigureOpenXRLoader();
                PlayerSettings.colorSpace = ColorSpace.Linear;
                PlayerSettings.productName = "Project 08 - Surfing a Gravitational Wave";
                AssetDatabase.SaveAssets();
                ValidateGeneratedScenes();
                Debug.Log("[Project8] Scene build finished successfully.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Project8] Build failed: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw; // interactive callers (VisualFixRunner) must see the failure
            }
        }

        static void ValidateGeneratedScenes()
        {
            foreach (string sceneName in new[] { "Main" })
            {
                var scene = EditorSceneManager.OpenScene($"{Root}/Scenes/{sceneName}.unity");
                var objects = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
                foreach (string required in new[] { "Spaceship", "Starfield", "Earth", "ControlPanel", "MenuRail", "SpacetimeSheet", "SubtitleBar", "RickAvatar" })
                    if (!objects.Any(t => t.name == required))
                        throw new InvalidOperationException($"{sceneName}: missing {required}");
                var starRenderer = objects.Select(t => t.GetComponent<ParticleSystemRenderer>())
                    .FirstOrDefault(r => r != null && r.name == "Starfield");
                if (starRenderer == null || starRenderer.sharedMaterial == null ||
                    starRenderer.sharedMaterial.mainTexture == null)
                    throw new InvalidOperationException($"{sceneName}: stars have no sprite material");
                if (objects.Any(t => t.name == "Console" || t.name.StartsWith("Label_grab me")))
                    throw new InvalidOperationException($"{sceneName}: cockpit is obstructed");
                var cockpit = objects.Select(t => t.GetComponent<CockpitControls>())
                    .FirstOrDefault(component => component != null);
                if (cockpit == null || cockpit.controlPanel.activeSelf ||
                    cockpit.secondaryPanel.activeSelf || !cockpit.compactPanel.activeSelf)
                    throw new InvalidOperationException($"{sceneName}: drawer panels must start closed");
                var sheet = objects.Select(t => t.GetComponent<SpacetimeSheet>())
                    .FirstOrDefault(component => component != null);
                if (sheet == null || sheet.wells == null || sheet.wells.Length == 0 ||
                    sheet.sheetMaterial == null)
                    throw new InvalidOperationException($"{sceneName}: incomplete spacetime sheet");
                if (objects.Any(t => t.name == "AtmosphereGlow"))
                    throw new InvalidOperationException(
                        $"{sceneName}: occluding atmosphere shell must not be generated");
                var ifo = objects.Select(t => t.GetComponent<LaserInterferometer>())
                    .FirstOrDefault(component => component != null);
                if (ifo == null || ifo.mirrorA == null || ifo.mirrorB == null ||
                    ifo.outputBeam == null || ifo.photodetector == null)
                    throw new InvalidOperationException($"{sceneName}: incomplete interferometer");
                var origin = objects.Select(t => t.GetComponent<Unity.XR.CoreUtils.XROrigin>())
                    .FirstOrDefault(component => component != null);
                if (origin == null || !origin.name.Contains("XR Rig") || origin.Camera == null)
                    throw new InvalidOperationException($"{sceneName}: seated VR rig or camera missing");
                if (RenderSettings.skybox == null)
                    throw new InvalidOperationException($"{sceneName}: no space skybox material");
                foreach (var canvas in objects.Select(t => t.GetComponent<Canvas>())
                             .Where(c => c != null && c.renderMode == RenderMode.WorldSpace))
                    if (Vector3.Dot(canvas.transform.forward,
                            canvas.transform.position - origin.Camera.transform.position) <= 0f)
                        throw new InvalidOperationException($"{sceneName}: mirrored panel {canvas.name}");
                if (sceneName == "Main")
                {
                    var story = objects.Select(t => t.GetComponent<StoryDirector>())
                        .FirstOrDefault(component => component != null);
                    if (story == null || story.startButton == null || story.continueButton == null ||
                        story.startButton.onClick.GetPersistentEventCount() == 0 ||
                        story.continueButton.onClick.GetPersistentEventCount() == 0)
                        throw new InvalidOperationException("Main: mission buttons not connected");
                }
                Debug.Log($"[Project8] Validated {sceneName}: VR rig, readable floating UI, star sprite, 3D spacetime lattice.");
            }
            var welcome = EditorSceneManager.OpenScene($"{Root}/Scenes/Welcome.unity");
            var settings = welcome.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ExperienceSettings>(true))
                .FirstOrDefault();
            if (settings == null || settings.enterButton == null ||
                settings.enterButton.onClick.GetPersistentEventCount() == 0 ||
                RenderSettings.skybox == null)
                throw new InvalidOperationException("Welcome: language selection or entry button missing");
            Debug.Log("[Project8] Validated Welcome: language selection before Main.");
        }

        static double _smokeStart;
        const string SmokeSessionKey = "Project8.PlaySmokeActive";

        [InitializeOnLoadMethod]
        static void ResumeSmokeAfterReload()
        {
            if (SessionState.GetBool(SmokeSessionKey, false))
                EditorApplication.update += CheckSmokeScene;
        }

        public static void VerifyPlayMode()
        {
            EditorSceneManager.OpenScene($"{Root}/Scenes/Main.unity");
            SessionState.SetBool(SmokeSessionKey, true);
            EditorApplication.update += CheckSmokeScene;
            EditorApplication.isPlaying = true;
        }

        static void CheckSmokeScene()
        {
            if (!EditorApplication.isPlaying) return;
            if (_smokeStart <= 0d) _smokeStart = EditorApplication.timeSinceStartup;
            double elapsed = EditorApplication.timeSinceStartup - _smokeStart;

            var sheet = UnityEngine.Object.FindAnyObjectByType<SpacetimeSheet>();
            var mesh = sheet != null ? sheet.GetComponent<MeshFilter>().sharedMesh : null;
            var stars = GameObject.Find("Starfield")?.GetComponent<ParticleSystem>();
            var waveModel = UnityEngine.Object.FindAnyObjectByType<GravitationalWaveModel>();
            var ifo = UnityEngine.Object.FindAnyObjectByType<LaserInterferometer>();
            // Physics regression spot-checks at t = 0: radial emission must not have
            // started (emissionDelay = inspiral), so strain is zero everywhere and
            // the dark port sits dark; no occluding atmosphere shell may exist.
            var earthGO = GameObject.Find("Earth");
            bool physicsOk = waveModel != null && waveModel.radialPropagation &&
                waveModel.emissionDelay > 0f && earthGO != null &&
                waveModel.StrainAt(earthGO.transform.position, 0f) == 0f &&
                ifo != null && ifo.mirrorA != null && ifo.mirrorB != null &&
                Mathf.Approximately(ifo.DarkPortIntensity, 0f) &&
                GameObject.Find("AtmosphereGlow") == null;
            bool ready = mesh != null && mesh.vertexCount >= 2000 &&
                         stars != null && stars.particleCount >= 500 && physicsOk;
            if (!ready)
            {
                // Play-mode startup in batch mode is slow; keep polling until the deadline.
                if (elapsed < 25d) return;
                EditorApplication.update -= CheckSmokeScene;
                SessionState.SetBool(SmokeSessionKey, false);
                Debug.LogError("[Project8] Play smoke failed: spacetime sheet, textured " +
                               "stars or physics checks did not report within 25s " +
                               $"(mesh={(mesh != null ? mesh.vertexCount : -1)}, " +
                               $"stars={(stars != null ? stars.particleCount : -1)}, " +
                               $"physicsOk={physicsOk})");
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.update -= CheckSmokeScene;
            SessionState.SetBool(SmokeSessionKey, false);
            Debug.Log($"[Project8] Play smoke passed: {mesh.vertexCount} sheet vertices, " +
                      $"{stars.particleCount} stars.");
            EditorApplication.Exit(0);
        }

        static void LoadBuiltins()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            _knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        }

        static void EnsureFolders()
        {
            foreach (var leaf in new[] { "Scenes", "Scripts", "Editor", "Prefabs",
                                         "Materials", "Settings", "Audio", "Documentation" })
                if (!AssetDatabase.IsValidFolder($"{Root}/{leaf}"))
                    AssetDatabase.CreateFolder(Root, leaf);
            if (!AssetDatabase.IsValidFolder($"{Root}/Materials/UI"))
                AssetDatabase.CreateFolder($"{Root}/Materials", "UI");
        }

        // ------------------------------------------------------------------ assets
        struct Mats
        {
            public Material TestMass, Handle;
            public Material Hull, HullDark, Console, Glass, EngineGlow;
            public Material NetLine, BlackHole, Accretion, AccretionGlow, Planet, StarGlow, Laser;
            public Material ReturnBeam;
        }

        static Mats CreateMaterials()
        {
            var m = new Mats();
            m.TestMass = MakeMaterial("M_TestMass", "Standard", new Color(0.92f, 0.92f, 0.95f), 0.35f);
            bool freshHandle = !MaterialExists("M_StrainHandle");
            m.Handle = MakeMaterial("M_StrainHandle", "Standard", new Color(0.15f, 0.7f, 0.75f), 0.5f);
            if (freshHandle)
            {
                m.Handle.EnableKeyword("_EMISSION");
                m.Handle.SetColor("_EmissionColor", new Color(0.02f, 0.25f, 0.28f));
            }
            m.Hull = MakeMaterial("M_Hull", "Standard", new Color(0.55f, 0.57f, 0.63f), 0.55f);
            m.HullDark = MakeMaterial("M_HullDark", "Standard", new Color(0.10f, 0.11f, 0.14f), 0.35f);
            m.Console = MakeMaterial("M_Console", "Standard", new Color(0.16f, 0.17f, 0.21f), 0.5f);
            bool freshGlass = !MaterialExists("M_Glass");
            m.Glass = MakeMaterial("M_Glass", "Standard", new Color(0.6f, 0.8f, 0.95f, 0.07f), 0.95f);
            if (freshGlass) SetupTransparent(m.Glass);
            bool freshEngine = !MaterialExists("M_EngineGlow");
            m.EngineGlow = MakeMaterial("M_EngineGlow", "Standard", new Color(0.15f, 0.85f, 0.9f), 0.6f);
            if (freshEngine)
            {
                m.EngineGlow.EnableKeyword("_EMISSION");
                m.EngineGlow.SetColor("_EmissionColor", new Color(0.1f, 1.5f, 1.6f));
            }

            m.NetLine = MakeMaterial("M_NetLine", "Legacy Shaders/Particles/Additive", Color.white);
            m.NetLine.SetColor("_TintColor", new Color(0.3f, 0.8f, 1f, 0.45f));
            m.BlackHole = MakeMaterial("M_BlackHole", "Standard", new Color(0.012f, 0.008f, 0.016f), 0.98f);
            bool freshAccretion = !MaterialExists("M_Accretion");
            m.Accretion = MakeMaterial("M_Accretion", "Standard", new Color(0.9f, 0.45f, 0.1f), 0.4f);
            if (freshAccretion)
            {
                m.Accretion.EnableKeyword("_EMISSION");
                m.Accretion.SetColor("_EmissionColor", new Color(2.4f, 1.0f, 0.28f));
            }
            m.AccretionGlow = MakeMaterial("M_AccretionGlow", "Legacy Shaders/Particles/Additive", Color.white);
            m.AccretionGlow.SetColor("_TintColor", new Color(1f, 0.7f, 0.3f));
            m.Planet = MakeMaterial("M_Planet", "Standard", new Color(0.32f, 0.46f, 0.72f), 0.72f);
            bool freshStar = !MaterialExists("M_Star");
            m.StarGlow = MakeMaterial("M_Star", "Standard", new Color(1f, 0.95f, 0.8f), 0f);
            if (freshStar)
            {
                m.StarGlow.EnableKeyword("_EMISSION");
                m.StarGlow.SetColor("_EmissionColor", new Color(3f, 2.6f, 2f));
            }
            m.Laser = MakeMaterial("M_Laser", "Legacy Shaders/Particles/Additive", Color.white);
            m.Laser.SetColor("_TintColor", new Color(1f, 0.1f, 0.1f, 0.8f));
            // Return path of the interferometer beams: cyan, so outbound vs inbound
            // light stays visually distinct.
            m.ReturnBeam = MakeMaterial("M_ReturnBeam", "Legacy Shaders/Particles/Additive", Color.white);
            m.ReturnBeam.SetColor("_TintColor", new Color(0.25f, 0.9f, 1f, 0.8f));
            return m;
        }

        static void SetupTransparent(Material m)
        {
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static bool MaterialExists(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/{name}.mat") != null;

        static Material MakeMaterial(string name, string shader, Color color, float smooth = 0f)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find(shader));
            mat.color = color;
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smooth);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static TestMass CreateTestMassPrefab(Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "TestMass";
            go.transform.localScale = Vector3.one * 0.075f;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<TestMass>();
            string path = $"{Root}/Prefabs/TestMass.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab.GetComponent<TestMass>();
        }

        static SimulationParameters CreateParameters(string assetName, WaveMode mode)
        {
            string path = $"{Root}/Settings/{assetName}.asset";
            var p = AssetDatabase.LoadAssetAtPath<SimulationParameters>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<SimulationParameters>();
                AssetDatabase.CreateAsset(p, path);
            }
            // Teaching-model defaults (see Documentation/PhysicsModel.md)
            p.strainAmplitude = 0.5f;
            p.frequencyHz = 0.55f;
            p.phaseRadians = 0f;
            // +X: the binary sits left-front of the ship and Earth right-front,
            // so the wave sweeps laterally across the seated view.
            p.propagationAxis = PropagationAxis.PositiveX;
            p.waveMode = mode;
            p.packetDurationSeconds = 4f;
            p.inspiralDurationSeconds = 8f;
            p.ringdownSeconds = 4f;
            p.postMergerResidual = 0.25f;
            p.visualizationSpeedMps = 3.2f;
            p.displacementExaggeration = 1.6f;
            EditorUtility.SetDirty(p);
            return p;
        }

        static TutorKnowledgeBase CreateKnowledgeBase()
        {
            string path = $"{Root}/Settings/TutorKnowledgeBase.asset";
            var kb = AssetDatabase.LoadAssetAtPath<TutorKnowledgeBase>(path);
            if (kb == null)
            {
                kb = ScriptableObject.CreateInstance<TutorKnowledgeBase>();
                AssetDatabase.CreateAsset(kb, path);
            }
            kb.entries = new System.Collections.Generic.List<TutorKnowledgeBase.Entry>
            {
                E(new[]{"black hole","binary","merger","chirp","inspiral","orbit"},
                  "The two dark spheres with glowing orange accretion discs are a black-hole " +
                  "binary. They orbit faster and faster as they spiral inward, then merge - " +
                  "the flash marks coalescence, when the emitted strain peaks: a 'chirp' " +
                  "signal that sweeps upward in frequency and amplitude, like GW150914."),
                E(new[]{"net","lattice","web","lines","grid"},
                  "The web of spheres in front of the ship is a lattice of test masses. As " +
                  "the wavefront sweeps through it you can see the links stretch and squeeze " +
                  "alternately - that is how a detector 'sees' a gravitational wave."),
                E(new[]{"einstein","telescope","et ","triangle","detector","interferometer","ligo"},
                  "The triangular facility at the end of the mission is the Einstein " +
                  "Telescope model: three interferometers in an equilateral triangle, each " +
                  "station's arms at 60 degrees, and each station nesting a low- and a " +
                  "high-frequency instrument - the real 2020 design baseline. Like LIGO's " +
                  "Michelsons it splits laser light, sends it down two arms and recombines " +
                  "it at the splitter; a wave shifts each round trip differently so the " +
                  "dark ports read differently. Our model shows the principle only - no " +
                  "cavities, suspensions or noise curves."),
                E(new[]{"lf","hf","low frequency","high frequency","lane","xylophone"},
                  "Each ET corner station actually hosts TWO interferometers: one " +
                  "cryogenic low-frequency instrument and one high-frequency instrument - " +
                  "the 'xylophone' layout. In this app the two lanes are drawn separately " +
                  "but share one simplified strain response; the real instruments cover " +
                  "different frequency bands with different noise curves."),

                E(new[]{"strain","delta l","relative change"},
                  "Strain h = deltaL / L is the relative change in separation between two free " +
                  "test masses. If they are 1 m apart and the wave changes the distance by " +
                  "0.01 m, the strain is 0.01. Real detector strain is around 1e-21."),
                E(new[]{"stretch","squeeze","polarization","ellipse","deform"},
                  "A passing gravitational wave alternately stretches space in one transverse " +
                  "direction and squeezes it in the perpendicular one - the '+' polarization. " +
                  "Watch the ring of test masses deform into an ellipse that flips orientation."),
                E(new[]{"wavefront","marker","reached","ahead","behind"},
                  "The yellow ring marks where the disturbance has reached right now " +
                  "(s = v*t along the propagation axis). The green and red markers ride just " +
                  "ahead of and behind it so you can predict which mass moves next."),
                E(new[]{"speed","slow","light","fast","velocity"},
                  "Real gravitational waves travel at the speed of light - you could never " +
                  "watch or surf one. This app only slows the VISUALIZATION speed; the model " +
                  "itself is unchanged. That is a viewing choice, not a physical claim."),
                E(new[]{"exaggerat","scale","real","small","actually"},
                  "Real GW strain on Earth is about 1e-21 - utterly invisible. Following " +
                  "LIGO's public visualization practice, the deformation shown here is " +
                  "enormously exaggerated so the pattern can be seen at all."),
                E(new[]{"ride","surf","perspective","view","frame","camera"},
                  "Riding alongside the wave is a pedagogical viewpoint, not a real " +
                  "relativistic observer frame. It lets you inspect propagation; the physics " +
                  "parameters stay the same - only your viewpoint changes."),
                E(new[]{"frequency","pitch","cycles"},
                  "Frequency f sets how many stretch-and-squeeze cycles occur per second of " +
                  "simulation time. Raise it and the ring oscillates faster between ellipses."),
                E(new[]{"amplitude","h0","strength","how big"},
                  "h0 is the strain amplitude - the maximum relative deformation. Bigger h0 " +
                  "means a deeper stretch and a harder squeeze as the wave passes."),
                E(new[]{"invariant","same","change","compare"},
                  "When you switch viewpoints, the propagation direction, frequency and " +
                  "amplitude stay the same - they are model parameters. What changes is only " +
                  "your reference frame, a visualization choice."),
                E(new[]{"mass","particle","ring","object","sphere","grid"},
                  "Each sphere is a freely floating test mass. A wave passing between them " +
                  "changes their separations - that relative change is all a detector can see."),
                E(new[]{"pause","replay","predict","task","reset"},
                  "Pause mid-flight and predict which mass will move next; the wavefront " +
                  "marker shows what the wave has reached. Replay starts the wave over."),
                E(new[]{"direction","propagat","travel"},
                  "The wave travels along a fixed axis through the field. Reconstruct it by " +
                  "watching the order in which test masses at different positions respond."),
            };
            EditorUtility.SetDirty(kb);
            return kb;
        }

        static TutorKnowledgeBase.Entry E(string[] kw, string answer)
            => new TutorKnowledgeBase.Entry { keywords = kw, answer = answer };

        static AudioClip CreateHumClip()
        {
            string path = $"{Root}/Audio/WaveHum.asset";
            var existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (existing != null) return existing;
            const int sr = 22050;
            var data = new float[sr];
            for (int i = 0; i < sr; i++)
                data[i] = 0.6f * Mathf.Sin(2f * Mathf.PI * 110f * i / sr);
            var clip = AudioClip.Create("WaveHum", sr, 1, sr, false);
            clip.SetData(data, 0);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // ------------------------------------------------------------------ scene
        static void BuildWelcomeScene(Mats mats, AudioClip hum)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.18f, 0.23f);
            RenderSettings.skybox = CreateSpaceSkybox();
            BuildStarfield(CreateStarMaterial());
            var ship = BuildSpaceship(mats, hum);
            var rig = InstantiateXRRig();
            rig.transform.SetParent(ship.transform, false);
            rig.transform.localPosition = new Vector3(0f, 0f, -0.15f);
            var origin = rig.GetComponentInChildren<Unity.XR.CoreUtils.XROrigin>(true);
            origin.CameraYOffset = 1.25f;
            origin.CameraFloorOffsetObject.transform.localPosition = Vector3.up * 1.25f;
            origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Device;
            var camera = rig.GetComponentInChildren<Camera>(true);
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.cullingMask = -1;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 3000f;
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            camera.tag = "MainCamera";
            DisableSampleLocomotion(rig.transform);
            SetupInteractionManager();
            var cockpit = ship.AddComponent<CockpitControls>();
            cockpit.seatedCamera = camera;
            cockpit.xrRigRoot = rig.transform;
            cockpit.flightSpeed = 0f;
            ship.AddComponent<Project8.Diagnostics.VRDiagnostics>();
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            cockpit.xrInputModule = (Behaviour)AddComponentByNames(es,
                "UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit");
            cockpit.desktopInputModule = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            cockpit.desktopInputModule.AssignDefaultActions();
            cockpit.desktopInputModule.enabled = false;

            var panel = new GameObject("WelcomePanel");
            var wcanvas = panel.AddComponent<Canvas>();
            wcanvas.renderMode = RenderMode.WorldSpace;
            wcanvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            panel.AddComponent<GraphicRaycaster>();
            AddComponentByNames(panel,
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            var rt = (RectTransform)panel.transform;
            rt.sizeDelta = new Vector2(1000f, 650f);
            rt.SetParent(ship.transform, false);
            rt.localPosition = new Vector3(0f, 1.4f, 1.12f);
            rt.localScale = Vector3.one * 0.0012f;
            rt.rotation = Quaternion.LookRotation(rt.position - camera.transform.position, Vector3.up);
            PanelHitArea(rt);
            GlassBG(rt, GlassRole.Panel, GlassPanelTint);
            var settings = panel.AddComponent<ExperienceSettings>();
            MakeText(rt, "WelcomeTitle", "SURFING A GRAVITATIONAL WAVE", 32, FontStyle.Bold,
                TextAnchor.MiddleCenter, Color.white, new Vector2(0f, 235f), new Vector2(920f, 50f));
            MakeText(rt, "WelcomeIntro", "Sit inside the ship and watch two black holes merge.\n" +
                "Choose your language before entering the story.", 19, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.9f, 1f),
                new Vector2(0f, 130f), new Vector2(880f, 100f));
            MakeButton(rt, "Btn_English", "English", new Vector2(-300f, 15f), 230f, settings.ChooseEnglish);
            var chinese = MakeButton(rt, "Btn_Chinese", "中文", new Vector2(0f, 15f), 230f,
                settings.ChooseChinese);
            settings.chineseButtonText = chinese;
            MakeButton(rt, "Btn_Dutch", "Nederlands", new Vector2(300f, 15f), 230f,
                settings.ChooseDutch);
            settings.selectionText = MakeText(rt, "Selection", "", 18, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.82f, 0.45f),
                new Vector2(0f, -95f), new Vector2(900f, 50f));
            var enter = MakeButton(rt, "Btn_Enter", "ENTER EXPERIENCE", new Vector2(0f, -200f),
                330f, settings.BeginExperience);
            settings.enterButton = enter.transform.parent.GetComponent<Button>();
            foreach (var c in scene.GetRootGameObjects()
                         .SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                if (c.renderMode == RenderMode.WorldSpace) c.worldCamera = camera;
            EditorSceneManager.SaveScene(scene, $"{Root}/Scenes/Welcome.unity");
        }

        static void BuildScene(Mats mats, TestMass massPrefab, SimulationParameters parameters,
                               TutorKnowledgeBase knowledge, AudioClip hum, string sceneName)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Vector3 seatPos = new Vector3(0f, 0f, -0.15f);
            Vector3 eye = seatPos + new Vector3(0f, 1.25f, 0f);
            // Side-on viewing geometry: the binary sits left-front, the wave radiates
            // outward (+X dominates) across the view, Earth is ~45 degrees to the right
            // and the Michelson detector lies beyond it. The binary is sunk to y=-3.8 so
            // the sheet's deepest trough (-5.5) bites into the holes' lower halves; Earth
            // floats at y=6.8 so its bottom (-0.2) just touches the shallow sheet dent.
            Vector3 binaryPos = new Vector3(-16f, -3.8f, 22f);
            Vector3 earthPos = new Vector3(52f, 6.8f, 22f);

            // --- space environment --------------------------------------------
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.09f, 0.11f, 0.16f);
            RenderSettings.fog = false;
            RenderSettings.skybox = CreateSpaceSkybox();

            var starGO = new GameObject("Starlight");
            var starLight = starGO.AddComponent<Light>();
            starLight.type = LightType.Directional;
            starLight.intensity = 0.85f;
            starLight.color = new Color(0.85f, 0.9f, 1f);
            starLight.shadows = LightShadows.Soft;
            starGO.transform.rotation = Quaternion.Euler(35f, -130f, 0f);

            BuildStarfield(CreateStarMaterial());
            BuildNebulae();
            BuildPlanetAndStar(mats);

            // --- simulation core ----------------------------------------------
            var simGO = new GameObject("Simulation");
            simGO.AddComponent<EntryGate>();
            var model = simGO.AddComponent<GravitationalWaveModel>();
            model.parameters = parameters;
            // Concentric emission from the binary, born at the merger flash: nothing
            // exists anywhere before t = inspiralDurationSeconds (inspiral radiation
            // intentionally excluded - see Documentation/PhysicsModel.md). The fixed
            // +X axis is kept for the scripted ride choreography.
            model.radialPropagation = true;
            model.emissionDelay = parameters.inspiralDurationSeconds;
            var controller = simGO.AddComponent<SimulationController>();
            controller.parameters = parameters;

            var originGO = new GameObject("WaveOrigin (at the binary)");
            originGO.transform.SetParent(simGO.transform, false);
            originGO.transform.position = binaryPos;
            model.waveOrigin = originGO.transform;

            var loggerGO = new GameObject("ResearchLogger");
            var logger = loggerGO.AddComponent<ResearchLogger>();
            logger.simulation = controller;

            var tutorGO = new GameObject("AITutor");
            var tutor = tutorGO.AddComponent<AITutor>();
            tutor.knowledgeBase = knowledge;
            // NVIDIA NIM free tier (key read from env var OPENAI_API_KEY at runtime;
            // falls back to the local knowledge base when no key/server is available).
            tutor.endpoint = "https://integrate.api.nvidia.com/v1/chat/completions";
            tutor.model = "openai/gpt-oss-20b";
            tutor.apiKeyEnvironmentVariable = "OPENAI_API_KEY";
            var voice = tutorGO.AddComponent<Project8.UI.RickVoice>();

            // --- binary ---------------------------------------------------------
            var binary = BuildBinaryBlackHoles(mats, controller, binaryPos,
                parameters.inspiralDurationSeconds);

            // --- Earth -----------------------------------------------------------
            var earth = BuildEarth(mats, earthPos, 7f);
            var deformer = earth.GetComponent<EarthDeformer>();
            deformer.model = model;
            deformer.simulation = controller;

            // --- spacetime "bedsheet" ---------------------------------------------
            // One flexible translucent membrane replaces the old multi-layer lattice:
            // masses press smooth gaussian dents into it - the black holes deep and
            // narrow, Earth shallow and wide - and the wave ripples across the cloth.
            var sheetGO = new GameObject("SpacetimeSheet");
            var sheet = sheetGO.AddComponent<SpacetimeSheet>();
            sheet.model = model;
            sheet.simulation = controller;
            sheetGO.transform.position = new Vector3(18f, 0f, 22f);
            sheet.sheetMaterial = MakeMaterial("M_SpacetimeSheet", "Project8/SpacetimeSheet", Color.white);
            sheet.size = new Vector2(160f, 78f);
            // Flat rest surface at y=1.5: the 7 m wells under the 1.9 m-radius holes
            // bottom out at -5.5 (into their lower halves), Earth's 1.7 m dent
            // reaches -0.2 - exactly the planet's underside.
            sheet.baseHeight = 1.5f;
            sheet.flexibility = 1f;
            sheet.wells = new[]
            {
                new SpacetimeSheet.GravityWell { source = binary.blackHoleA, depth = 7f, radius = 3f },
                new SpacetimeSheet.GravityWell { source = binary.blackHoleB, depth = 7f, radius = 3f },
                new SpacetimeSheet.GravityWell { source = binary.mergedHole, depth = 7f, radius = 4f },
                new SpacetimeSheet.GravityWell { source = earth.transform, depth = 1.7f, radius = 8f },
            };

            var fieldGO = new GameObject("PredictionTestMasses");
            fieldGO.transform.position = new Vector3(-5f, 1.8f, 15f);
            var field = fieldGO.AddComponent<TestMassField>();
            field.model = model;
            field.simulation = controller;
            field.testMassPrefab = massPrefab;
            field.layout = TestMassField.FieldLayout.Volume;
            field.gridSize = 3;
            field.gridSpacing = 3f;
            field.depthCount = 5;
            field.depthSpacing = 3f;

            // --- spaceship + seated rig -----------------------------------------
            var ship = BuildSpaceship(mats, hum);
            // Spawn already parked at the 90-degree vantage: south of the
            // binary-Earth line, facing +Z straight at it. No approach flight -
            // the mission starts with the binary dead ahead.
            ship.transform.position = new Vector3(-16f, 1.5f, 4f);
            var rig = InstantiateXRRig();
            rig.transform.SetParent(ship.transform, false);
            rig.transform.localPosition = seatPos;
            var xrOrigin = rig.GetComponentInChildren<Unity.XR.CoreUtils.XROrigin>(true);
            if (xrOrigin != null)
            {
                xrOrigin.CameraYOffset = 1.25f;
                xrOrigin.CameraFloorOffsetObject.transform.localPosition = Vector3.up * 1.25f;
                // Floor-origin tracking can sink a seated user inside the hull when the
                // guardian floor is not where Unity expects it. Device origin keeps the
                // head pose relative to the HMD itself - safe for a seated experience.
                xrOrigin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Device;
            }
            var seatCamera = rig.GetComponentInChildren<Camera>(true);
            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.backgroundColor = new Color(0.008f, 0.01f, 0.02f);
                cam.cullingMask = -1;
                cam.nearClipPlane = 0.02f;
                cam.farClipPlane = 3000f;
                cam.stereoTargetEye = StereoTargetEyeMask.Both;
                cam.tag = "MainCamera";
            }
            simGO.AddComponent<Project8.Diagnostics.VRDiagnostics>();
            DisableSampleLocomotion(rig.transform);
            SetupInteractionManager();
            var cockpit = ship.AddComponent<CockpitControls>();
            cockpit.seatedCamera = seatCamera;
            cockpit.xrRigRoot = rig.transform;

            // --- perspective -----------------------------------------------------
            var perspGO = new GameObject("PerspectiveController");
            var perspective = perspGO.AddComponent<PerspectiveController>();
            perspective.model = model;
            perspective.simulation = controller;
            perspective.xrRigRoot = FindRigRoot(rig.transform);
            perspective.moveTarget = ship.transform;
            perspective.rideSideOffset = 4.5f;
            perspective.rideHeightOffset = 0.6f;
            perspective.enabled = false; // StoryDirector owns ship motion until the user rides

            // --- prediction task --------------------------------------------------
            var taskGO = new GameObject("PredictionTask");
            var task = taskGO.AddComponent<PredictionTask>();
            task.field = field;
            task.model = model;
            task.simulation = controller;

            // --- the Einstein Telescope observatory -------------------------------
            // Horizontal equilateral triangle of three 60-degree stations, each
            // hosting a nested LF/HF lane pair (2020 design baseline). It sits
            // beyond Earth on the wave line - the mission's final stop.
            // Panels face the eye in WORLD space - the ship no longer sits at the origin.
            Vector3 eyeWorld = ship.transform.TransformPoint(eye);
            var et = BuildEinsteinTelescope(mats, model, controller,
                new Vector3(82f, 1.5f, 26f), eyeWorld);

            // --- cockpit UI -------------------------------------------------------
            // One shared subtitle strip (story narration + Rick's answers), a
            // persistent Play/Pause dock, the mission action dock, and separate
            // drawers: CONTROLS (wave shape, slots, transport, ET inspector) and
            // MISSION & HELP (the honest-limits disclosure). No floating prose
            // labels anywhere in the world.
            var subtitles = BuildSubtitleCanvas(ship.transform, eyeWorld);
            var story = BuildStoryDirector(ship.transform, eyeWorld, controller, model,
                earth.transform, binary.transform, et.transform, voice, subtitles);
            cockpit.story = story;
            var panel = BuildControlPanel(ship.transform, eyeWorld, controller, model,
                parameters, perspective, task, tutor, voice, et, story);
            cockpit.controlPanel = panel.gameObject;
            cockpit.secondaryPanel = BuildMissionHelp(ship.transform, eyeWorld, cockpit);
            BuildPlaybackDock(ship.transform, eyeWorld, controller, story);
            cockpit.compactPanel = BuildCompactPanel(ship.transform, cockpit, eyeWorld);
            BuildRickAvatar(ship.transform, eyeWorld, tutor, voice, subtitles);
            task.ui = panel;
            panel.gameObject.SetActive(false);
            cockpit.secondaryPanel.SetActive(false);

            // --- grabbable strain handle on the left console wing --------------------
            var anchor = new GameObject("StrainHandle_Anchor");
            anchor.transform.SetParent(ship.transform, false);
            anchor.transform.localPosition = new Vector3(-0.95f, 0.75f, -0.25f);
            var handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handle.name = "StrainHandle";
            handle.transform.SetParent(ship.transform, false);
            handle.transform.localPosition = anchor.transform.localPosition;
            handle.transform.localScale = Vector3.one * 0.08f;
            handle.GetComponent<Renderer>().sharedMaterial = mats.Handle;
            var rb = handle.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            var grabParam = handle.AddComponent<GrabToParameter>();
            grabParam.parameters = parameters;
            grabParam.restAnchor = anchor.transform;
            AddComponentByNames(handle,
                "UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable, Unity.XR.Interaction.Toolkit",
                "UnityEngine.XR.Interaction.Toolkit.XRGrabInteractable, Unity.XR.Interaction.Toolkit");
            // --- event system ---------------------------------------------------------
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            cockpit.xrInputModule = (Behaviour)AddComponentByNames(es,
                "UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit");
            cockpit.desktopInputModule = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            cockpit.desktopInputModule.AssignDefaultActions();
            cockpit.desktopInputModule.enabled = false;

            // World-space canvases need an event camera or ScreenPointToLocalPoint
            // fails and sliders cannot be dragged.
            foreach (var c in scene.GetRootGameObjects()
                         .SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                if (c.renderMode == RenderMode.WorldSpace) c.worldCamera = seatCamera;

            // --- save ------------------------------------------------------------------
            string scenePath = $"{Root}/Scenes/{sceneName}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[Project8] Scene saved: {scenePath}");
        }

        // ------------------------------------------------------------- earth
        static Transform BuildEarth(Mats mats, Vector3 pos, float radius)
        {
            var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            e.name = "Earth";
            e.transform.position = pos;
            e.transform.localScale = Vector3.one * radius * 2f;
            e.GetComponent<Renderer>().sharedMaterial = CreateEarthMaterial();
            KillCollider(e);
            // No atmosphere shell: the previous additive "glow" sphere rendered as
            // an opaque shroud that hid the whole Blue Marble surface.
            var d = e.AddComponent<EarthDeformer>();
            d.visualGain = 0.35f;
            return e.transform;
        }

        static Material CreateEarthMaterial()
        {
            const string path = Root + "/Materials/M_Earth.mat";
            const string texPath = Root + "/Textures/Earth/earth-blue-marble.jpg";
            // NASA Blue Marble imagery (public domain) via the three.js examples repo.
            // 2:1 equirectangular map: longitude wraps, latitude clamps.
            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer != null && (importer.wrapModeU != TextureWrapMode.Repeat ||
                                     importer.wrapModeV != TextureWrapMode.Clamp))
            {
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Project8/EarthSurface")) { name = "M_Earth" };
                AssetDatabase.CreateAsset(mat, path);
            }
            // Update the EXISTING material in place: matte diffuse shader, full-globe
            // UVs (tiling 1, offset 0), no specular/emission.
            var shader = Shader.Find("Project8/EarthSurface");
            if (shader != null && mat.shader != shader) mat.shader = shader;
            if (tex != null) mat.mainTexture = tex;
            else mat.color = new Color(0.15f, 0.35f, 0.75f); // fallback ocean blue
            mat.mainTextureScale = Vector2.one;
            mat.mainTextureOffset = Vector2.zero;
            if (mat.HasProperty("_AmbientFloor")) mat.SetFloat("_AmbientFloor", 0.45f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Transform MakeLightMarker(Transform parent, string name, Mats mats)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.16f;
            go.GetComponent<Renderer>().sharedMaterial = mats.EngineGlow;
            KillCollider(go);
            return go.transform;
        }

        static LineRenderer MakeBeam(Transform parent, string name, Vector3 a, Vector3 b,
                                     Material mat, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.widthMultiplier = width;
            lr.sharedMaterial = mat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        // ------------------------------------------------------------- spaceship
        static GameObject BuildSpaceship(Mats mats, AudioClip hum)
        {
            var ship = new GameObject("Spaceship");
            ship.transform.position = Vector3.zero;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            floor.transform.SetParent(ship.transform, false);
            floor.transform.localPosition = Vector3.zero;
            floor.transform.localScale = new Vector3(2.3f, 0.03f, 2.3f);
            floor.GetComponent<Renderer>().sharedMaterial = mats.Hull;

            var skirt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            skirt.name = "HullSkirt";
            skirt.transform.SetParent(ship.transform, false);
            skirt.transform.localPosition = new Vector3(0f, -0.24f, 0f);
            skirt.transform.localScale = new Vector3(2.8f, 0.14f, 2.8f);
            skirt.GetComponent<Renderer>().sharedMaterial = mats.HullDark;

            var engine = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            engine.name = "EngineGlow";
            engine.transform.SetParent(ship.transform, false);
            engine.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            engine.transform.localScale = new Vector3(1.1f, 0.015f, 1.1f);
            engine.GetComponent<Renderer>().sharedMaterial = mats.EngineGlow;
            KillCollider(engine);

            var engLight = new GameObject("EngineLight");
            engLight.transform.SetParent(ship.transform, false);
            engLight.transform.localPosition = new Vector3(0f, -0.55f, 0f);
            var el = engLight.AddComponent<Light>();
            el.type = LightType.Point;
            el.color = new Color(0.2f, 0.9f, 1f);
            el.intensity = 0.8f;
            el.range = 4f;

            var seat = new GameObject("Seat");
            seat.transform.SetParent(ship.transform, false);
            seat.transform.localPosition = new Vector3(0f, 0f, -0.15f);
            MakeBox(seat.transform, "Post", new Vector3(0f, 0.16f, 0f),
                new Vector3(0.16f, 0.3f, 0.16f), mats.HullDark);
            MakeBox(seat.transform, "Cushion", new Vector3(0f, 0.34f, 0f),
                new Vector3(0.5f, 0.12f, 0.5f), mats.HullDark);
            MakeBox(seat.transform, "Back", new Vector3(0f, 0.68f, -0.24f),
                new Vector3(0.5f, 0.62f, 0.1f), mats.HullDark, Quaternion.Euler(-10f, 0f, 0f));
            MakeBox(seat.transform, "ArmL", new Vector3(-0.32f, 0.52f, 0f),
                new Vector3(0.07f, 0.09f, 0.46f), mats.HullDark);
            MakeBox(seat.transform, "ArmR", new Vector3(0.32f, 0.52f, 0f),
                new Vector3(0.07f, 0.09f, 0.46f), mats.HullDark);

            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "GlassDome";
            dome.transform.SetParent(ship.transform, false);
            dome.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            dome.transform.localScale = Vector3.one * 2.7f;
            dome.GetComponent<Renderer>().sharedMaterial = mats.Glass;
            KillCollider(dome);

            foreach (float az in new[] { 120f, 180f, 240f })
            {
                float a = az * Mathf.Deg2Rad;
                Vector3 rim = new Vector3(Mathf.Sin(a) * 1.05f, 0.06f, Mathf.Cos(a) * 1.05f);
                MakeStrut(ship.transform, "Strut_" + az, rim, new Vector3(0f, 1.85f, 0f),
                    0.022f, mats.Hull);
            }

            var lightGO = new GameObject("CockpitLight");
            lightGO.transform.SetParent(ship.transform, false);
            lightGO.transform.localPosition = new Vector3(0f, 1.7f, 0.1f);
            var cl = lightGO.AddComponent<Light>();
            cl.type = LightType.Point;
            cl.color = new Color(1f, 0.85f, 0.65f);
            cl.intensity = 0.9f;
            cl.range = 3.5f;

            var src = ship.AddComponent<AudioSource>();
            src.clip = hum;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 0.07f;
            src.playOnAwake = true;

            return ship;
        }

        static GameObject MakeBox(Transform parent, string name, Vector3 localPos, Vector3 size,
                                  Material mat, Quaternion? localRot = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            if (localRot.HasValue) go.transform.localRotation = localRot.Value;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static void MakeStrut(Transform parent, string name, Vector3 from, Vector3 to,
                              float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            Vector3 dir = to - from;
            go.transform.localPosition = (from + to) * 0.5f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            go.transform.localScale = new Vector3(radius * 2f, dir.magnitude * 0.5f, radius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            KillCollider(go);
        }

        static void KillCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);
        }

        // ------------------------------------------------------------- space dressing
        static Material CreateSpaceSkybox()
        {
            string archivePath = $"{Root}/Textures/kenney_skyboxes-space.zip";
            string textureRoot = $"{Root}/Textures/Kenney";
            if (!System.IO.Directory.Exists(textureRoot)) System.IO.Directory.CreateDirectory(textureRoot);
            if (System.IO.File.Exists(archivePath))
            {
                using (var archive = System.IO.Compression.ZipFile.OpenRead(archivePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string extension = System.IO.Path.GetExtension(entry.Name).ToLowerInvariant();
                        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg") continue;
                        string destination = textureRoot + "/" + entry.Name;
                        if (System.IO.File.Exists(destination)) continue;
                        using (var input = entry.Open())
                        using (var output = System.IO.File.Create(destination)) input.CopyTo(output);
                        AssetDatabase.ImportAsset(destination);
                        Debug.Log($"[Project8] Imported Kenney CC0 skybox texture: {destination}");
                    }
                }
            }

            Texture2D selected = null;
            int bestScore = -1;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { textureRoot }))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (tex == null || tex.width != tex.height * 2) continue;
                string name = tex.name.ToLowerInvariant();
                int score = tex.width + (name.Contains("dark") ? 10000 : 0) +
                            (name.Contains("galaxy") ? 3000 : 0) +
                            (name.Contains("nebula") ? 2000 : 0);
                if (score > bestScore) { bestScore = score; selected = tex; }
            }
            if (selected == null)
            {
                Debug.LogWarning("[Project8] Kenney archive had no 2:1 panorama; using generated Milky Way backdrop.");
                selected = CreateFallbackPanorama();
            }
            string materialPath = $"{Root}/Materials/M_KenneySpaceSkybox.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(mat, materialPath);
            }
            mat.SetTexture("_MainTex", selected);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D CreateFallbackPanorama()
        {
            string path = $"{Root}/Textures/SpaceFallback.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int width = 1024, height = 512;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float band = Mathf.Exp(-Mathf.Pow((y - height * 0.53f -
                    25f * Mathf.Sin(x * 0.012f)) / 65f, 2f));
                float cloud = Mathf.PerlinNoise(x * 0.017f, y * 0.035f);
                float dust = Mathf.PerlinNoise(x * 0.043f + 19f, y * 0.055f + 8f);
                float glow = band * Mathf.Max(0f, cloud - dust * 0.55f);
                var baseColor = new Color(0.008f, 0.012f, 0.033f);
                pixels[y * width + x] = baseColor + new Color(0.21f, 0.24f, 0.43f) * glow;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material CreateStarMaterial()
        {
            string archivePath = $"{Root}/Textures/kenney_particle-pack.zip";
            string texturePath = $"{Root}/Textures/Kenney/star-particle.png";
            if (!System.IO.File.Exists(texturePath) && System.IO.File.Exists(archivePath))
            {
                using (var archive = System.IO.Compression.ZipFile.OpenRead(archivePath))
                {
                    var entry = archive.Entries.FirstOrDefault(e =>
                        e.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                        e.Name.IndexOf("star", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        e.FullName.IndexOf("PNG", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (entry == null)
                        throw new InvalidOperationException("Kenney particle pack has no star PNG sprite");
                    using (var input = entry.Open())
                    using (var output = System.IO.File.Create(texturePath)) input.CopyTo(output);
                    AssetDatabase.ImportAsset(texturePath);
                    Debug.Log($"[Project8] Star sprite from Kenney Particle Pack (CC0): {entry.FullName}");
                }
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) throw new InvalidOperationException("Kenney star sprite is missing");
            string materialPath = $"{Root}/Materials/M_StarParticle.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.mainTexture = texture;
            material.SetColor("_TintColor", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void BuildStarfield(Material starMaterial)
        {
            var go = new GameObject("Starfield");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1e5f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.55f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f), new Color(0.65f, 0.78f, 1f));
            main.maxParticles = 3000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 3000f) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 150f;
            shape.radiusThickness = 0.95f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = starMaterial;
        }

        static void BuildNebulae()
        {
            var tex = CreateNebulaTexture();
            var defs = new (Vector3 pos, Color col, float size)[]
            {
                (new Vector3(-95f, 45f, 90f),   new Color(0.5f, 0.2f, 0.7f, 0.13f),  120f),
                (new Vector3(85f, -35f, 75f),   new Color(0.15f, 0.45f, 0.6f, 0.11f), 95f),
                (new Vector3(-40f, 15f, -120f), new Color(0.7f, 0.3f, 0.2f, 0.09f),  140f),
                (new Vector3(60f, 55f, -70f),   new Color(0.3f, 0.35f, 0.75f, 0.08f),  90f),
            };
            foreach (var (pos, col, size) in defs)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Nebula";
                q.transform.position = pos;
                q.transform.localScale = Vector3.one * size;
                // Quad renders on its local -Z face: aim that back toward the ship.
                q.transform.rotation = Quaternion.LookRotation(pos.normalized);
                var r = q.GetComponent<Renderer>();
                var m = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
                m.mainTexture = tex;
                m.SetColor("_TintColor", col);
                r.sharedMaterial = m;
                KillCollider(q);
            }
        }

        static Texture2D CreateNebulaTexture()
        {
            string path = $"{Root}/Textures/nebula.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(S * 0.5f, S * 0.5f)) / (S * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                a *= a;
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply();

            System.IO.Directory.CreateDirectory($"{Root}/Textures");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void BuildPlanetAndStar(Mats mats)
        {
            var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planet.name = "DistantPlanet";
            planet.transform.position = new Vector3(80f, -30f, 115f);
            planet.transform.localScale = Vector3.one * 26f;
            planet.GetComponent<Renderer>().sharedMaterial = mats.Planet;
            KillCollider(planet);

            var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            star.name = "DistantStar";
            star.transform.position = new Vector3(-85f, 50f, -55f);
            star.transform.localScale = Vector3.one * 5f;
            star.GetComponent<Renderer>().sharedMaterial = mats.StarGlow;
            KillCollider(star);
        }

        // ------------------------------------------------------------- binary black holes
        static BlackHoleBinary BuildBinaryBlackHoles(Mats mats, SimulationController sim,
                                                     Vector3 com, float inspiral)
        {
            var root = new GameObject("BinaryBlackHoles");
            root.transform.position = com;
            var binary = root.AddComponent<BlackHoleBinary>();
            binary.simulation = sim;
            binary.inspiralDuration = inspiral;
            binary.startSeparation = 11f;
            // No orbital-plane tilt: the bedsheet analogy wants the pair orbiting
            // exactly in the sheet's horizontal plane.
            binary.orbitTiltDeg = 0f;

            // Sphere-primitive scale 3.8 -> 1.9 m radius: the sheet's -5.5 trough
            // intersects each hole's lower half (centre -3.8) while the tops stay exposed.
            binary.blackHoleA = MakeBlackHole(mats, "BH_A", new Vector3(-5.5f, 0f, 0f), root.transform, 3.8f);
            binary.blackHoleB = MakeBlackHole(mats, "BH_B", new Vector3(5.5f, 0f, 0f), root.transform, 3.8f);
            binary.discA = binary.blackHoleA.Find("AccretionDisc");
            binary.discB = binary.blackHoleB.Find("AccretionDisc");
            // Remnant scale 4.8 -> 2.4 m radius: its bottom at -6.2 sits inside the
            // sheet's merged -5.5 trough, and it visibly outweighs the 1.9 m precursors.
            binary.mergedHole = MakeBlackHole(mats, "MergedHole", Vector3.zero, root.transform, 4.8f);
            binary.mergedHole.gameObject.SetActive(false);

            var flashGO = new GameObject("MergerFlash");
            flashGO.transform.SetParent(root.transform, false);
            flashGO.transform.localPosition = Vector3.zero;
            var flash = flashGO.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.75f, 0.4f);
            flash.intensity = 0f;
            flash.range = 80f;
            binary.mergerFlash = flash;

            return binary;
        }

        static Mesh CreateAccretionMesh()
        {
            string path = $"{Root}/Prefabs/AccretionDisc.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            const int segments = 96, bands = 8;
            var vertices = new Vector3[(bands + 1) * (segments + 1)];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[bands * segments * 6];
            for (int r = 0; r <= bands; r++)
            for (int a = 0; a <= segments; a++)
            {
                int i = r * (segments + 1) + a;
                float radial = r / (float)bands;
                float angle = a * 2f * Mathf.PI / segments;
                float radius = Mathf.Lerp(0.62f, 2.6f, radial);
                vertices[i] = new Vector3(Mathf.Cos(angle) * radius,
                    0.035f * Mathf.Sin(angle * 3f + radial * 10f), Mathf.Sin(angle) * radius);
                uv[i] = new Vector2(a / (float)segments, radial);
                float brightness = Mathf.Sin(Mathf.PI * radial) *
                    (0.72f + 0.28f * Mathf.Sin(angle * 7f + radial * 20f));
                colors[i] = new Color(1f, Mathf.Lerp(0.45f, 0.12f, radial), 0.06f, brightness * 0.88f);
                if (r == bands || a == segments) continue;
                int t = (r * segments + a) * 6;
                int next = i + segments + 1;
                triangles[t] = i;
                triangles[t + 1] = next;
                triangles[t + 2] = i + 1;
                triangles[t + 3] = i + 1;
                triangles[t + 4] = next;
                triangles[t + 5] = next + 1;
            }
            var mesh = new Mesh { name = "AccretionDisc" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Transform MakeBlackHole(Mats mats, string name, Vector3 localPos, Transform parent,
                                       float scale)
        {
            var hole = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hole.name = name;
            hole.transform.SetParent(parent, false);
            hole.transform.localPosition = localPos;
            hole.transform.localScale = Vector3.one * scale;
            hole.GetComponent<Renderer>().sharedMaterial = mats.BlackHole;
            KillCollider(hole);

            var disc = new GameObject("AccretionDisc");
            disc.transform.SetParent(hole.transform, false);
            disc.transform.localRotation = Quaternion.Euler(14f, 0f, 9f);
            disc.AddComponent<MeshFilter>().sharedMesh = CreateAccretionMesh();
            disc.AddComponent<MeshRenderer>().sharedMaterial = mats.AccretionGlow;

            var halo = new GameObject("PhotonRing");
            halo.transform.SetParent(hole.transform, false);
            halo.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            var ring = halo.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = 96;
            ring.widthMultiplier = 0.045f;
            ring.sharedMaterial = mats.AccretionGlow;
            for (int i = 0; i < ring.positionCount; i++)
            {
                float a = i * 2f * Mathf.PI / ring.positionCount;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.6f, 0f) * 0.68f);
            }

            var glowGO = new GameObject("Glow");
            glowGO.transform.SetParent(hole.transform, false);
            glowGO.transform.localPosition = Vector3.zero;
            var glow = glowGO.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.6f, 0.25f);
            glow.intensity = 1.4f;
            glow.range = 9f;

            return hole.transform;
        }

        // ------------------------------------------------------------- device simulator
        static void InstantiateDeviceSimulator()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Samples" });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (name.IndexOf("Device Simulator", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (name.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0) continue; // skip the UI variant
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.name = "XR Device Simulator (keyboard/mouse)";
                // Spawned inactive on purpose: when a real headset is connected the
                // simulator's fake HMD/controller devices hijack every action
                // binding - dead rays, dead sticks, frozen head. CockpitControls
                // activates it only after confirming no XR device is present.
                inst.SetActive(false);
                Debug.Log($"[Project8] Device simulator instantiated (inactive): {name}");
                return;
            }
            Debug.LogWarning("[Project8] XR Device Simulator prefab not found - run " +
                             "'Project 8 > Install XR Packages' first to import the sample.");
        }

        static Transform FindRigRoot(Transform rig)
        {
            // The sample "XR Interaction Setup" contains a child "XR Origin (XR Rig)";
            // that node is the one to move in ride mode.
            var xrOrigin = rig.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.IndexOf("XR Origin", StringComparison.OrdinalIgnoreCase) >= 0);
            return xrOrigin != null ? xrOrigin : rig;
        }

        /// <summary>The starter rig already ships tracked NearFar interactors under
        /// "Left Controller"/"Right Controller", plus a full locomotion stack (Move,
        /// Turn/Snap Turn, Teleport, Climb, Grab Move, Jump, Gravity) bound to the
        /// same thumbsticks CockpitControls reads. Both systems reading the sticks
        /// produces double translation and opposing yaw, and teleport rays pop out
        /// when the stick is pushed - so every sample locomotion object and provider
        /// is disabled; the ship is flown exclusively by CockpitControls.</summary>
        static void DisableSampleLocomotion(Transform rig)
        {
            string[] names =
            {
                "Locomotion", "Move", "Turn", "Gravity", "Teleportation",
                "Climb", "Climb Teleport", "Grab Move", "Jump"
            };
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
                if (names.Any(n => string.Equals(t.name, n, StringComparison.OrdinalIgnoreCase)) ||
                    t.name.IndexOf("Teleport", StringComparison.OrdinalIgnoreCase) >= 0)
                    t.gameObject.SetActive(false);
            foreach (var b in rig.GetComponentsInChildren<Behaviour>(true))
            {
                string tn = b.GetType().Name;
                if (tn.IndexOf("LocomotionMediator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tn.IndexOf("LocomotionProvider", StringComparison.OrdinalIgnoreCase) >= 0)
                    b.enabled = false;
            }
        }

        // -------------------------------------------------------- interaction setup
        /// <summary>
        /// Adds the XR Interaction Manager (mediates interactors/interactables) and an
        /// InputActionManager wired to the XRI Default Input Actions asset so the rig's
        /// action references actually receive input.
        /// </summary>
        static void SetupInteractionManager()
        {
            var managerGO = new GameObject("XR Interaction Manager");
            AddComponentByNames(managerGO,
                "UnityEngine.XR.Interaction.Toolkit.XRInteractionManager, Unity.XR.Interaction.Toolkit");

            var inputGO = new GameObject("Input Action Manager (XRInputBootstrap)");
            var bootstrap = inputGO.AddComponent<XRInputBootstrap>();
            var asset = FindInputActionsAsset();
            if (asset != null)
            {
                bootstrap.actions = asset;
                Debug.Log("[Project8] XRInputBootstrap wired to default input actions.");
            }
            else
            {
                Debug.LogWarning("[Project8] No .inputactions asset found - assign one on " +
                                 "XRInputBootstrap or enable actions manually.");
            }
        }

        static UnityEngine.InputSystem.InputActionAsset FindInputActionsAsset()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:InputActionAsset", new[] { "Assets/Samples" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("XRI Default Input Actions", StringComparison.OrdinalIgnoreCase) >= 0)
                    return AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(path);
            }
            // fall back to any .inputactions in the project
            var any = AssetDatabase.FindAssets("t:InputActionAsset", new[] { "Assets" });
            return any.Length > 0
                ? AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(
                    AssetDatabase.GUIDToAssetPath(any[0]))
                : null;
        }

        // ------------------------------------------------------------------ XR rig
        static GameObject InstantiateXRRig()
        {
            // Prefer the fully wired "XR Interaction Setup" prefab from the XRI sample.
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Samples" });
            GameObject best = null;
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("/Starter Assets/Prefabs/XR Origin (XR Rig).prefab")) continue;
                best = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                break;
            }
            if (best != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(best);
                instance.name = best.name;
                Debug.Log($"[Project8] XR rig instantiated from sample prefab: {best.name}");
                return instance;
            }

            Debug.LogWarning("[Project8] No XRI sample prefab found - creating fallback camera rig. " +
                             "Import 'Starter Assets' from the XRI package for a full rig.");
            var rig = new GameObject("XR Origin (Fallback)");
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(rig.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = camGO.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            camGO.AddComponent<AudioListener>();
            AddComponentByNames(camGO, "UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem");
            return rig;
        }

        static void MakeFloatingLabel(string text, Transform parent, Vector3 pos,
                                      float charSize, Color color, Vector3 viewer)
        {
            var go = new GameObject("Label_" + (text.Length > 16 ? text.Substring(0, 16) : text));
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = _font;
            tm.fontSize = 48;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            // TextMesh renders readable on its local -Z side; Billboard keeps that side
            // facing the camera forever, so labels can never appear mirrored.
            go.transform.rotation = Quaternion.LookRotation((pos - viewer).normalized, Vector3.up);
            go.GetComponent<MeshRenderer>().sharedMaterial = _font != null ? _font.material : null;
            go.AddComponent<Project8.UI.Billboard>();
        }

        // ------------------------------------------------------------- UI helpers
        static RectTransform NewUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static string ChineseText(string english)
        {
            switch (english)
            {
                case "MISSION": return "任务";
                case "MISSION  <": return "任务  <";
                case "WAVE LAB  <": return "波形实验室  <";
                case "LAB PANEL  <": return "实验面板  <";
                case "BACK TO STORY": return "返回任务剧情";
                case "CONTROLS  <": return "控制台  <";
                case "CONTROLS": return "控制台";
                case "AI tutor - answers appear as subtitles": return "AI 助教 - 回答显示为字幕";
                case "ASK": return "提问";
                case "MIC": return "语音";
                case "PROJECT 08  -  SURFING A GRAVITATIONAL WAVE": return "项目 08  -  冲浪引力波";
                case "START MISSION": return "开始任务";
                case "CONTINUE": return "继续";
                case "RESTART MISSION": return "重新开始任务";
                case "CLOSE  X": return "收起  X";
                case "Pause": return "暂停";
                case "Replay": return "重播";
                case "Reset": return "重置";
                case "Ride with the wave": return "跟随引力波";
                case "Predict next mass": return "预测下一个小球";
                case "ASK RICK": return "提问 RICK";
                case "strain amplitude h0": return "应变振幅 h0";
                case "frequency f (Hz)": return "频率 f（赫兹）";
                case "CONTROL PANEL  -  shape the wave, save it, replay it":
                    return "控制台  -  调整波形、保存、回放";
                case "CONTROL CONSOLE": return "控制台";
                case "WAVE LAB": return "波形实验";
                case "MISSION & HELP": return "任务与帮助";
                case "WAVE SHAPE": return "波形参数";
                case "SAVED WAVES": return "已存波形";
                case "REPLAY CURRENT WAVE": return "重播当前波";
                case "RESET": return "重置";
                case "CONFIRM OVERWRITE": return "确认覆盖";
                case "CANCEL": return "取消";
                case "DETAIL ON/OFF": return "细节 开/关";
                case "MISSION TOOLS": return "任务工具";
                case "RICK · A2": return "RICK · A2";
                case "Save wave": return "保存波形";
                case "Replay wave": return "回放波形";
                case "click a slot: empty = save current wave, saved = load it": return "点击空槽保存波形，点击已存槽加载波形";
                case "exaggeration: 0 = real scale (invisible)  ->  2 = max": return "形变夸张度：0 = 实际尺度（不可见），2 = 最大";
                case "the laser Michelson rig beyond Earth is a LIGO-style teaching cutaway - the mission ends beside it":
                    return "地球后方的激光迈克耳孙装置是 LIGO 式教学剖面模型，任务最后一站在它旁边";
                default:
                    if (english.StartsWith("Wave ") && english.EndsWith("  (empty)"))
                        return "波形 " + english.Substring(5, 1) + "（空）";
                    if (english.StartsWith("Simplified teaching model:"))
                        return "简化教学模型：形变经过夸张，传播速度被放慢；真实引力波以光速传播。";
                    if (english.StartsWith("orange tint =") ||
                        english.StartsWith("orange = stretch"))
                        return "橙色=拉伸  |  蓝色=挤压  |  波前经过处节点变亮";
                    if (english.StartsWith("3 stations - 60-degree arms"))
                        return "3 个测站 - 60 度臂 - 每站一对嵌套 LF/HF - 图中 12 m 代表真实约 10 km";
                    return null;
            }
        }

        static string DutchText(string english)
        {
            switch (english)
            {
                case "MISSION": return "MISSIE";
                case "MISSION  <": return "MISSIE  <";
                case "WAVE LAB  <": return "GOLVENLAB  <";
                case "LAB PANEL  <": return "LABPANEEL  <";
                case "BACK TO STORY": return "TERUG NAAR VERHAAL";
                case "CONTROLS  <": return "BEDIENING  <";
                case "CONTROLS": return "BEDIENING";
                case "AI tutor - answers appear as subtitles":
                    return "AI-tutor - antwoorden verschijnen als ondertitels";
                case "ASK": return "VRAAG";
                case "MIC": return "MIC";
                case "PROJECT 08  -  SURFING A GRAVITATIONAL WAVE":
                    return "PROJECT 08  -  SURFEN OP EEN ZWAARTEKRACHTGOLF";
                case "START MISSION": return "MISSIE STARTEN";
                case "CONTINUE": return "DOORGAAN";
                case "RESTART MISSION": return "MISSIE HERSTARTEN";
                case "CLOSE  X": return "SLUITEN  X";
                case "Pause": return "Pauze";
                case "Replay": return "Opnieuw";
                case "Reset": return "Resetten";
                case "Ride with the wave": return "Surfen op de golf";
                case "Predict next mass": return "Voorspel volgende massa";
                case "ASK RICK": return "VRAAG AAN RICK";
                case "strain amplitude h0": return "rek-amplitude h0";
                case "frequency f (Hz)": return "frequentie f (Hz)";
                case "CONTROL PANEL  -  shape the wave, save it, replay it":
                    return "BEDIENINGSPANEEL  -  vorm de golf, sla op, speel af";
                case "CONTROL CONSOLE": return "BEDIENING";
                case "WAVE LAB": return "GOLFLAB";
                case "MISSION & HELP": return "MISSIE & HELP";
                case "WAVE SHAPE": return "GOLFVORM";
                case "SAVED WAVES": return "OPGESLAGEN GOLVEN";
                case "REPLAY CURRENT WAVE": return "HUIDIGE GOLF OPNIEUW";
                case "RESET": return "RESET";
                case "CONFIRM OVERWRITE": return "OVERSCHRIJVEN";
                case "CANCEL": return "ANNULEREN";
                case "DETAIL ON/OFF": return "DETAIL AAN/UIT";
                case "MISSION TOOLS": return "MISSIE-TOOLS";
                case "Save wave": return "Golf opslaan";
                case "Replay wave": return "Golf afspelen";
                case "click a slot: empty = save current wave, saved = load it":
                    return "klik op een slot: leeg = huidige golf opslaan, opgeslagen = laden";
                case "exaggeration: 0 = real scale (invisible)  ->  2 = max":
                    return "overdrijving: 0 = echte schaal (onzichtbaar)  ->  2 = max";
                case "the laser Michelson rig beyond Earth is a LIGO-style teaching cutaway - the mission ends beside it":
                    return "het laser-Michelson-toestel voorbij de Aarde is een LIGO-achtig leermodel - de missie eindigt ernaast";
                default:
                    if (english.StartsWith("Wave ") && english.EndsWith("  (empty)"))
                        return "Golf " + english.Substring(5, 1) + "  (leeg)";
                    if (english.StartsWith("Simplified teaching model:"))
                        return "Vereenvoudigd lesmodel: deformatie is OVERDREVEN en de voortplanting " +
                               "is VERTRAAGD - echte zwaartekrachtgolven gaan met lichtsnelheid.";
                    if (english.StartsWith("orange tint =") ||
                        english.StartsWith("orange = stretch"))
                        return "oranje = uitrekken  |  blauw = samentrekken  |  knopen lichten op bij het golffront";
                    if (english.StartsWith("3 stations - 60-degree arms"))
                        return "3 stations - 60-graden armen - genest LF/HF-paar per station - " +
                               "12 m getoond staat voor ~10 km echte armen";
                    return null;
            }
        }

        static Text MakeText(Transform parent, string name, string content, int size,
                             FontStyle style, TextAnchor anchor, Color color,
                             Vector2 pos, Vector2 dims)
        {
            var rt = NewUIObject(name, parent);
            rt.anchoredPosition = pos;
            rt.sizeDelta = dims;
            var text = rt.gameObject.AddComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // Labels never take pointer input - otherwise the value texts that overlap
            // the sliders turn parts of the track into dead zones that cannot be dragged.
            text.raycastTarget = false;
            string chinese = ChineseText(content);
            string dutch = DutchText(content);
            if (!string.IsNullOrEmpty(chinese) || !string.IsNullOrEmpty(dutch))
            {
                var localized = rt.gameObject.AddComponent<LocalizedText>();
                localized.english = content;
                localized.chinese = chinese;
                localized.dutch = dutch;
            }
            return text;
        }

        static Text MakeButton(Transform parent, string name, string label,
                               Vector2 pos, float width, UnityAction onClick = null)
        {
            var rt = NewUIObject(name, parent);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 48f); // minimum touch height
            var img = rt.gameObject.AddComponent<LiquidGlassGraphic>();
            img.material = GlassMat(GlassRole.Button);
            img.color = GlassButtonTint;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.3f, 1f);
            colors.pressedColor = new Color(0.75f, 1.15f, 1.4f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.62f, 0.5f);
            btn.colors = colors;
            if (onClick != null) UnityEventTools.AddPersistentListener(btn.onClick, onClick);

            var text = MakeText(rt, "Label", label, 18, FontStyle.Bold, TextAnchor.MiddleCenter,
                Color.white, Vector2.zero, rt.sizeDelta);
            Stretch((RectTransform)text.transform);
            return text;
        }

        static InputField MakeInput(Transform parent, string name, Vector2 pos, float width,
                                    string hint)
        {
            var rt = NewUIObject(name, parent);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 48f);
            var img = rt.gameObject.AddComponent<LiquidGlassGraphic>();
            img.material = GlassMat(GlassRole.Inset);
            img.color = GlassInsetTint;
            var field = rt.gameObject.AddComponent<InputField>();

            var textRt = NewUIObject("Text", rt);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 4f);
            textRt.offsetMax = new Vector2(-12f, -4f);
            var text = textRt.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = 15;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            field.textComponent = text;

            var phRt = NewUIObject("Placeholder", rt);
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(12f, 4f);
            phRt.offsetMax = new Vector2(-12f, -4f);
            var ph = phRt.gameObject.AddComponent<Text>();
            ph.font = _font;
            ph.fontSize = 15;
            ph.fontStyle = FontStyle.Italic;
            ph.color = new Color(0.6f, 0.62f, 0.7f, 0.7f);
            ph.alignment = TextAnchor.MiddleLeft;
            ph.text = hint;
            field.placeholder = ph;
            return field;
        }

        static Slider MakeSlider(Transform parent, string name, Vector2 pos, float width,
                                 float min, float max, float value, UnityAction<float> onValue)
        {
            var rt = NewUIObject(name, parent);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 34f);
            // The slider itself needs a Graphic so its whole track is a pointer-hit zone;
            // without one you can only grab the tiny handle child.
            var trackImg = rt.gameObject.AddComponent<Image>();
            trackImg.raycastTarget = true;
            trackImg.color = new Color(1f, 1f, 1f, 0.02f);
            var slider = rt.gameObject.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.direction = Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.interactable = true;

            var bg = NewUIObject("Background", rt);
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<LiquidGlassGraphic>();
            bgImg.material = GlassMat(GlassRole.Inset);
            bgImg.color = GlassInsetTint;
            bgImg.raycastTarget = false;

            var fillArea = NewUIObject("Fill Area", rt);
            Stretch(fillArea);
            var fill = NewUIObject("Fill", fillArea);
            Stretch(fill);
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.sprite = _uiSprite;
            fillImg.type = Image.Type.Sliced;
            fillImg.color = new Color(Accent.r, Accent.g, Accent.b, 0.9f);
            slider.fillRect = fill;

            var handleArea = NewUIObject("Handle Slide Area", rt);
            Stretch(handleArea);
            var handle = NewUIObject("Handle", handleArea);
            handle.sizeDelta = new Vector2(34f, 40f);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.sprite = _knobSprite != null ? _knobSprite : _uiSprite;
            handleImg.color = Accent;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;

            slider.value = value;
            UnityEventTools.AddPersistentListener(slider.onValueChanged, onValue);
            return slider;
        }

        [MenuItem("Project 8/Diagnose Types")]
        public static void Diagnose()
        {
            var sb = new System.Text.StringBuilder();
#pragma warning disable UAC0005
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = asm.GetName().Name;
                if (n.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("XR", StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.AppendLine(n);
            }
#pragma warning restore UAC0005
            Debug.Log("[Diag] assemblies:" + Environment.NewLine + sb);

            foreach (var tn in new[]
            {
                "UnityEngine.InputSystem.InputActionManager",
                "UnityEngine.InputSystem.InputSystem",
                "UnityEngine.InputSystem.XR.TrackedPoseDriver",
                "UnityEngine.XR.Interaction.Toolkit.XRInteractionManager",
                "UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable",
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea",
                "UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule",
                "UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget",
                "UnityEditor.XR.Management.Metadata.XRPackageMetadataStore",
                "UnityEngine.XR.OpenXR.OpenXRSettings",
            })
            {
                var t = FindType(tn);
                Debug.Log($"[Diag] {tn} -> {(t != null ? t.Assembly.GetName().Name : "NULL")}");
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------- reflection
        static Component AddComponentByNames(GameObject go, params string[] assemblyQualifiedNames)
        {
            foreach (var name in assemblyQualifiedNames)
            {
                var t = Type.GetType(name) ?? FindType(name.Split(',')[0].Trim());
                if (t == null) continue;
                try { return go.AddComponent(t); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Project8] AddComponent({t.Name}) failed: {e.Message}");
                }
            }
            Debug.LogWarning($"[Project8] Component type not found: {assemblyQualifiedNames[0]}");
            return null;
        }

#pragma warning disable UAC0005 // one-shot editor builder: enumerating loaded
        // assemblies here has no leak consequence and works regardless of package versions.
        static Type FindType(string fullName)
        {
#pragma warning disable UAC0005
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
#pragma warning restore UAC0005

        // ------------------------------------------------------------- OpenXR setup
        // Reflection is used deliberately: the builder must still compile and run when the
        // XR packages are absent (e.g. first open before package resolution).
        static void ConfigureOpenXRLoader()
        {
            try
            {
                var perTargetType = FindType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget");
                if (perTargetType == null)
                {
                    Debug.LogWarning("[Project8] XR Management not present - skipping OpenXR setup.");
                    return;
                }

                // XRGeneralSettingsPerBuildTarget is a per-target container asset;
                // create it (and the Standalone settings) when the project has none yet.
                var getOrCreate = perTargetType.GetMethod("GetOrCreate",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                object perTarget = getOrCreate?.Invoke(null, null);
                var settingsFor = perTargetType.GetMethod("SettingsForBuildTarget");
                object general = settingsFor?.Invoke(perTarget, new object[] { BuildTargetGroup.Standalone });
                if (general == null)
                {
                    perTargetType.GetMethod("CreateDefaultSettingsForBuildTarget")
                        ?.Invoke(perTarget, new object[] { BuildTargetGroup.Standalone });
                    general = settingsFor?.Invoke(perTarget, new object[] { BuildTargetGroup.Standalone });
                }
                if (general == null)
                {
                    Debug.LogWarning("[Project8] Could not create XR general settings.");
                    return;
                }
                general.GetType().GetProperty("InitManagerOnStart")?.SetValue(general, true);

                var mgr = general.GetType().GetProperty("Manager")?.GetValue(general)
                          ?? general.GetType().GetProperty("AssignedSettings")?.GetValue(general);
                if (mgr == null)
                {
                    perTargetType.GetMethod("CreateDefaultManagerSettingsForBuildTarget")
                        ?.Invoke(perTarget, new object[] { BuildTargetGroup.Standalone });
                    general = settingsFor?.Invoke(perTarget, new object[] { BuildTargetGroup.Standalone });
                    mgr = general?.GetType().GetProperty("Manager")?.GetValue(general);
                }

                var store = FindType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore");
                var assign = store?.GetMethods()
                    .FirstOrDefault(m => m.Name == "AssignLoader" && m.IsStatic);
                if (mgr != null && assign != null)
                {
                    assign.Invoke(null, new object[]
                    {
                        mgr, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone
                    });
                    // XRManagerSettings.automaticLoading / automaticRunning must be on
                    // for the loader to actually initialize at app start.
                    mgr.GetType().GetProperty("automaticLoading")?.SetValue(mgr, true);
                    mgr.GetType().GetProperty("automaticRunning")?.SetValue(mgr, true);
                    EditorUtility.SetDirty((UnityEngine.Object)general);
                    EditorUtility.SetDirty((UnityEngine.Object)mgr);
                    EditorUtility.SetDirty((UnityEngine.Object)perTarget);
                    Debug.Log("[Project8] OpenXR loader enabled for the Standalone target.");
                }
                else
                {
                    Debug.LogWarning("[Project8] XR manager settings unavailable - loader not assigned.");
                }
                EnableOpenXRFeatures();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Project8] OpenXR setup skipped: {e.Message}");
            }
        }

                static void EnableOpenXRFeatures()
        {
            try
            {
                var settingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
                if (settingsType == null) return;

                object settings =
                    settingsType.GetMethod("GetSettingsForBuildTargetGroup")?
                        .Invoke(null, new object[] { BuildTargetGroup.Standalone })
                    ?? settingsType.GetProperty("Instance")?.GetValue(null);
                if (settings == null) return;

                var getFeature = settingsType.GetMethods()
                    .FirstOrDefault(m => m.Name == "GetFeature" && m.IsGenericMethodDefinition
                                         && m.GetParameters().Length == 0);
                if (getFeature == null) return;

                string[] profiles =
                {
                    "UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile",
                    "UnityEngine.XR.OpenXR.Features.Interactions.HTCViveControllerProfile",
                    "UnityEngine.XR.OpenXR.Features.Interactions.ValveIndexControllerProfile",
                    "UnityEngine.XR.OpenXR.Features.Interactions.MicrosoftMotionControllerProfile",
                };
                foreach (var name in profiles)
                {
                    var ft = FindType(name);
                    if (ft == null) continue;
                    try
                    {
                        var feature = getFeature.MakeGenericMethod(ft).Invoke(settings, null);
                        if (feature == null) continue;
                        ft.GetProperty("enabled")?.SetValue(feature, true);
                        EditorUtility.SetDirty((UnityEngine.Object)feature);
                        Debug.Log($"[Project8] OpenXR interaction profile enabled: {ft.Name}");
                    }
                    catch (Exception e) { Debug.LogWarning($"[Project8] {name}: {e.Message}"); }
                }
                EditorUtility.SetDirty((UnityEngine.Object)settings);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Project8] OpenXR feature setup skipped: {e.Message}");
            }
        }
    }
}
