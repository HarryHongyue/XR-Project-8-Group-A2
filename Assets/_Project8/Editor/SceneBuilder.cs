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
    public static class SceneBuilder
    {
        const string Root = "Assets/_Project8";

        static Font _font;
        static Sprite _uiSprite;
        static Sprite _knobSprite;

        [MenuItem("Project 8/Build Demo Scene")]
        public static void BuildAll()
        {
            try
            {
                LoadBuiltins();
                EnsureFolders();
                var mats = CreateMaterials();
                var prefab = CreateTestMassPrefab(mats.TestMass);
                var storyParams = CreateParameters("SimulationParameters", WaveMode.Chirp);
                var labParams = CreateParameters("SimulationParameters_Lab", WaveMode.Burst);
                var knowledge = CreateKnowledgeBase();
                var hum = CreateHumClip();
                BuildScene(mats, prefab, storyParams, knowledge, hum, true, "Main");
                BuildScene(mats, prefab, labParams, knowledge, hum, false, "WaveLab");
                BuildWelcomeScene(mats, hum);
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene($"{Root}/Scenes/Welcome.unity", true),
                    new EditorBuildSettingsScene($"{Root}/Scenes/Main.unity", true),
                    new EditorBuildSettingsScene($"{Root}/Scenes/WaveLab.unity", true),
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
            }
        }

        static void ValidateGeneratedScenes()
        {
            foreach (string sceneName in new[] { "Main", "WaveLab" })
            {
                var scene = EditorSceneManager.OpenScene($"{Root}/Scenes/{sceneName}.unity");
                var objects = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
                foreach (string required in new[] { "Spaceship", "Starfield", "Earth", "ControlPanel", "MenuRail", "SpacetimeVolume" })
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
                var volume = objects.Select(t => t.GetComponent<SpacetimeVolume>())
                    .FirstOrDefault(component => component != null);
                if (volume == null || volume.ship == null || volume.wells == null ||
                    volume.wells.Length == 0)
                    throw new InvalidOperationException($"{sceneName}: incomplete 3D spacetime field");
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

            var volume = UnityEngine.Object.FindAnyObjectByType<SpacetimeVolume>();
            var mesh = volume != null ? volume.GetComponent<MeshFilter>().sharedMesh : null;
            var points = volume != null ? volume.GetComponentInChildren<ParticleSystem>() : null;
            var stars = GameObject.Find("Starfield")?.GetComponent<ParticleSystem>();
            bool ready = mesh != null && mesh.vertexCount >= 2000 && points != null &&
                         points.particleCount >= 2000 &&
                         stars != null && stars.particleCount >= 500;
            if (!ready)
            {
                // Play-mode startup in batch mode is slow; keep polling until the deadline.
                if (elapsed < 25d) return;
                EditorApplication.update -= CheckSmokeScene;
                SessionState.SetBool(SmokeSessionKey, false);
                Debug.LogError("[Project8] Play smoke failed: spacetime volume or textured " +
                               "stars did not animate in Play mode within 25s " +
                               $"(mesh={(mesh != null ? mesh.vertexCount : -1)}, " +
                               $"points={(points != null ? points.particleCount : -1)}, " +
                               $"stars={(stars != null ? stars.particleCount : -1)})");
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.update -= CheckSmokeScene;
            SessionState.SetBool(SmokeSessionKey, false);
            Debug.Log($"[Project8] Play smoke passed: {mesh.vertexCount} lattice nodes, " +
                      $"{points.particleCount} test-mass particles, {stars.particleCount} stars.");
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
        }

        // ------------------------------------------------------------------ assets
        struct Mats
        {
            public Material TestMass, MarkerFront, MarkerAhead, MarkerBehind, Handle;
            public Material Hull, HullDark, Console, Glass, EngineGlow;
            public Material NetLine, BlackHole, Accretion, AccretionGlow, Planet, StarGlow, Laser;
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
            m.MarkerFront = MakeMaterial("M_MarkerFront", "Sprites/Default", new Color(1f, 0.85f, 0.1f, 0.9f));
            m.MarkerAhead = MakeMaterial("M_MarkerAhead", "Sprites/Default", new Color(0.25f, 0.9f, 0.35f, 0.65f));
            m.MarkerBehind = MakeMaterial("M_MarkerBehind", "Sprites/Default", new Color(0.95f, 0.25f, 0.25f, 0.65f));

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
            p.frequencyHz = 0.9f;
            p.phaseRadians = 0f;
            p.propagationAxis = PropagationAxis.NegativeZ;
            p.waveMode = mode;
            p.packetDurationSeconds = 4f;
            p.inspiralDurationSeconds = 8f;
            p.ringdownSeconds = 4f;
            p.postMergerResidual = 0.25f;
            p.visualizationSpeedMps = 2.2f;
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
            var camera = rig.GetComponentInChildren<Camera>(true);
            camera.clearFlags = CameraClearFlags.Skybox;
            SetupInteractionManager();
            var cockpit = ship.AddComponent<CockpitControls>();
            cockpit.seatedCamera = camera;
            cockpit.flightSpeed = 0f;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            cockpit.xrInputModule = (Behaviour)AddComponentByNames(es,
                "UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit");
            cockpit.desktopInputModule = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            cockpit.desktopInputModule.AssignDefaultActions();
            cockpit.desktopInputModule.enabled = false;

            var panel = new GameObject("WelcomePanel");
            panel.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            panel.AddComponent<GraphicRaycaster>();
            AddComponentByNames(panel,
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            var rt = (RectTransform)panel.transform;
            rt.sizeDelta = new Vector2(1000f, 650f);
            rt.SetParent(ship.transform, false);
            rt.localPosition = new Vector3(0f, 1.4f, 1.12f);
            rt.localScale = Vector3.one * 0.0012f;
            rt.rotation = Quaternion.LookRotation(rt.position - camera.transform.position, Vector3.up);
            var bg = NewUIObject("Background", rt);
            Stretch(bg);
            var img = bg.gameObject.AddComponent<Image>();
            img.sprite = _uiSprite;
            img.color = new Color(0.03f, 0.05f, 0.11f, 0.82f);
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
                               TutorKnowledgeBase knowledge, AudioClip hum, bool story, string sceneName)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Vector3 seatPos = new Vector3(0f, 0f, -0.15f);
            Vector3 eye = seatPos + new Vector3(0f, 1.25f, 0f);
            Vector3 binaryPos = new Vector3(0f, 1.8f, 23f);
            Vector3 earthPos = new Vector3(0f, -4f, -24f);

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

            // --- binary (story) or post-merger remnant (lab) ---------------------
            Transform[] wellSources;
            if (story)
            {
                var binary = BuildBinaryBlackHoles(mats, controller, binaryPos,
                    parameters.inspiralDurationSeconds);
                wellSources = new[] { binary.blackHoleA, binary.blackHoleB, binary.mergedHole };
            }
            else
            {
                wellSources = new[] { MakeBlackHole(mats, "RemnantHole", binaryPos, simGO.transform, 1.7f) };
            }

            // --- Earth (both scenes: story wave hits it, lab replays on it) ------
            var earth = BuildEarth(mats, earthPos, 7f);
            var deformer = earth.GetComponent<EarthDeformer>();
            deformer.model = model;
            deformer.simulation = controller;

            // --- test-mass "net" --------------------------------------------------
            var volumeGO = new GameObject("SpacetimeVolume");
            var volume = volumeGO.AddComponent<SpacetimeVolume>();
            volume.model = model;
            volume.simulation = controller;
            // Every mass dents the lattice: black holes deeply, Earth a little.
            var wells = new System.Collections.Generic.List<SpacetimeVolume.GravityWell>();
            foreach (var w in wellSources)
                wells.Add(new SpacetimeVolume.GravityWell { source = w, depth = 4.6f, radius = 30f });
            wells.Add(new SpacetimeVolume.GravityWell
                { source = earth.transform, depth = 1.2f, radius = 14f });
            volume.wells = wells.ToArray();
            volume.lineMaterial = mats.NetLine;
            volume.pointMaterial = CreateStarMaterial();
            volume.countX = 31;
            volume.countY = 11;
            volume.countZ = 33;
            volume.spacing = 2.6f;
            volume.centre = new Vector3(0f, 0.5f, 2f);
            volume.connectLayers = false;

            var fieldGO = new GameObject("PredictionTestMasses");
            fieldGO.transform.position = new Vector3(0f, 1.8f, 12f);
            var field = fieldGO.AddComponent<TestMassField>();
            field.model = model;
            field.simulation = controller;
            field.testMassPrefab = massPrefab;
            field.layout = TestMassField.FieldLayout.Volume;
            field.gridSize = 3;
            field.gridSpacing = 3f;
            field.depthCount = 5;
            field.depthSpacing = 3f;

            // --- wavefront markers ---------------------------------------------
            var markersGO = new GameObject("WavefrontMarkers");
            markersGO.transform.SetParent(simGO.transform, false);
            var markers = markersGO.AddComponent<WavefrontMarkers>();
            markers.model = model;
            markers.simulation = controller;
            markers.maxVisibleDistance = 70f;
            markers.markerOffset = 1.5f;
            markers.wavefrontMarker = MakeRingMarker("Marker_Wavefront", mats.MarkerFront,
                7.5f, "wavefront (where the disturbance has reached)", hum,
                model, controller).transform;
            markers.aheadMarker = MakeRingMarker("Marker_Ahead", mats.MarkerAhead,
                7.5f, "ahead of the wavefront").transform;
            markers.behindMarker = MakeRingMarker("Marker_Behind", mats.MarkerBehind,
                7.5f, "behind the wavefront").transform;

            // --- spaceship + seated rig -----------------------------------------
            var ship = BuildSpaceship(mats, hum);
            volume.ship = ship.transform;
            var rig = InstantiateXRRig();
            rig.transform.SetParent(ship.transform, false);
            rig.transform.localPosition = seatPos;
            var xrOrigin = rig.GetComponentInChildren<Unity.XR.CoreUtils.XROrigin>(true);
            if (xrOrigin != null)
            {
                xrOrigin.CameraYOffset = 1.25f;
                xrOrigin.CameraFloorOffsetObject.transform.localPosition = Vector3.up * 1.25f;
            }
            var seatCamera = rig.GetComponentInChildren<Camera>(true);
            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.backgroundColor = new Color(0.008f, 0.01f, 0.02f);
            }
            foreach (var component in rig.GetComponentsInChildren<Behaviour>(true))
            {
                string name = component.GetType().Name;
                if (name.Contains("MoveProvider") || name.Contains("TurnProvider") ||
                    name.Contains("TeleportationProvider")) component.enabled = false;
            }
            SetupInteractionManager();
            var cockpit = ship.AddComponent<CockpitControls>();
            cockpit.seatedCamera = seatCamera;

            // --- perspective -----------------------------------------------------
            var perspGO = new GameObject("PerspectiveController");
            var perspective = perspGO.AddComponent<PerspectiveController>();
            perspective.model = model;
            perspective.simulation = controller;
            perspective.xrRigRoot = FindRigRoot(rig.transform);
            perspective.moveTarget = ship.transform;
            perspective.rideSideOffset = 4.5f;
            perspective.rideHeightOffset = 0.6f;
            if (story) perspective.enabled = false; // StoryDirector owns ship motion here

            // --- prediction task --------------------------------------------------
            var taskGO = new GameObject("PredictionTask");
            var task = taskGO.AddComponent<PredictionTask>();
            task.field = field;
            task.model = model;
            task.simulation = controller;

            // --- console UI --------------------------------------------------------
            SimulationUI panel;
            if (story)
            {
                panel = BuildControlPanel(controller, model, parameters, perspective, task,
                    ship.transform, new Vector3(0.12f, 1.44f, 1.14f), eye);
                cockpit.controlPanel = panel.gameObject;
                cockpit.story = BuildMissionPanel(ship.transform, eye, controller, model, earth.transform, tutor);
                cockpit.secondaryPanel = cockpit.story.narrationText.GetComponentInParent<Canvas>().gameObject;
            }
            else
            {
                // Wave Lab uses ONE merged panel: transport + sliders + wave library.
                panel = BuildWaveLabPanel(ship.transform, eye, controller, model,
                    parameters, perspective, task, tutor);
                cockpit.controlPanel = panel.gameObject;
                cockpit.secondaryPanel = panel.gameObject;
                BuildInterferometer(mats, model, controller, new Vector3(3.4f, 1.35f, 5f));
            }
            task.ui = panel;
            cockpit.compactPanel = BuildCompactPanel(ship.transform, cockpit, eye, story);
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
            var atmo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            atmo.name = "AtmosphereGlow";
            atmo.transform.SetParent(e.transform, false);
            atmo.transform.localScale = Vector3.one * 1.07f;
            atmo.GetComponent<Renderer>().sharedMaterial = mats.NetLine;
            KillCollider(atmo);
            var d = e.AddComponent<EarthDeformer>();
            d.visualGain = 0.35f;
            return e.transform;
        }

        static Material CreateEarthMaterial()
        {
            const string path = Root + "/Materials/M_Earth.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            // NASA Blue Marble imagery (public domain) via the three.js examples repo.
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"{Root}/Textures/Earth/earth-blue-marble.jpg");
            var mat = new Material(Shader.Find("Standard")) { name = "M_Earth" };
            if (tex != null) mat.mainTexture = tex;
            else mat.color = new Color(0.15f, 0.35f, 0.75f); // fallback ocean blue
            mat.SetFloat("_Glossiness", 0.25f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ------------------------------------------------------------- interferometer
        static void BuildInterferometer(Mats mats, GravitationalWaveModel model,
                                        SimulationController sim, Vector3 pos)
        {
            var root = new GameObject("Interferometer (LIGO-style)");
            root.transform.position = pos;
            const float armLen = 1.9f;
            MakeBox(root.transform, "BeamSplitter", Vector3.zero,
                new Vector3(0.16f, 0.16f, 0.16f), mats.Console);
            MakeBox(root.transform, "MirrorX", new Vector3(armLen, 0f, 0f),
                new Vector3(0.07f, 0.24f, 0.07f), mats.Hull);
            MakeBox(root.transform, "MirrorZ", new Vector3(0f, 0f, armLen),
                new Vector3(0.07f, 0.24f, 0.07f), mats.Hull);
            MakeBox(root.transform, "RailX", new Vector3(armLen * 0.5f, -0.12f, 0f),
                new Vector3(armLen, 0.04f, 0.06f), mats.HullDark);
            MakeBox(root.transform, "RailZ", new Vector3(0f, -0.12f, armLen * 0.5f),
                new Vector3(0.06f, 0.04f, armLen), mats.HullDark);

            var armA = MakeLaserArm(root.transform, "LaserX", new Vector3(armLen, 0f, 0f), mats);
            var armB = MakeLaserArm(root.transform, "LaserZ", new Vector3(0f, 0f, armLen), mats);
            var li = root.AddComponent<LaserInterferometer>();
            li.model = model;
            li.simulation = sim;
            li.armA = armA;
            li.armB = armB;
        }

        static LineRenderer MakeLaserArm(Transform parent, string name, Vector3 end, Mats mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, Vector3.zero);
            lr.SetPosition(1, end);
            lr.widthMultiplier = 0.02f;
            lr.startColor = lr.endColor = new Color(1f, 0.12f, 0.12f, 0.9f);
            lr.material = mats.Laser;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        static GameObject BuildCompactPanel(Transform ship, CockpitControls cockpit, Vector3 eye, bool story)
        {
            var go = new GameObject("MenuRail");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<GraphicRaycaster>();
            AddComponentByNames(go,
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(250f, 170f);
            rt.SetParent(ship, false);
            rt.localPosition = new Vector3(-0.72f, 1.43f, 0.98f);
            rt.localScale = Vector3.one * 0.00115f;
            rt.rotation = Quaternion.LookRotation((rt.position - eye).normalized, Vector3.up);
            if (story)
            {
                MakeButton(rt, "Btn_Secondary", "MISSION  <",
                    new Vector2(0f, 48f), 235f, cockpit.ShowMission);
                MakeButton(rt, "Btn_Controls", "CONTROLS  <",
                    new Vector2(0f, -32f), 235f, cockpit.ShowControls);
            }
            else
            {
                MakeButton(rt, "Btn_Secondary", "LAB PANEL  <",
                    new Vector2(0f, 48f), 235f, cockpit.ShowControls);
                MakeButton(rt, "Btn_Back", "MISSION  <",
                    new Vector2(0f, -32f), 235f, cockpit.GoToStory);
            }
            return go;
        }

        // ------------------------------------------------------------- mission panel (story)
        static StoryDirector BuildMissionPanel(Transform ship, Vector3 eye, SimulationController sim,
                                      GravitationalWaveModel model, Transform earth, AITutor tutor)
        {
            var dirGO = new GameObject("StoryDirector");
            var director = dirGO.AddComponent<StoryDirector>();
            director.simulation = sim;
            director.model = model;
            director.ship = ship;
            director.earth = earth;
            director.cockpit = ship.GetComponent<CockpitControls>();
            director.flyTarget = new Vector3(0f, 0.9f, 7f);

            var canvasGO = new GameObject("MissionPanel");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<GraphicRaycaster>();
            AddComponentByNames(canvasGO,
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            var rt = (RectTransform)canvasGO.transform;
            rt.sizeDelta = new Vector2(900f, 480f);
            canvasGO.transform.SetParent(ship, true);
            canvasGO.transform.localPosition = new Vector3(0.12f, 1.44f, 1.14f);
            canvasGO.transform.localScale = Vector3.one * 0.00125f;
            canvasGO.transform.rotation =
                Quaternion.LookRotation((canvasGO.transform.position - eye).normalized, Vector3.up);

            var bg = NewUIObject("Background", rt);
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = _uiSprite;
            bgImg.color = new Color(0.03f, 0.05f, 0.10f, 0.78f);
            bgImg.type = Image.Type.Sliced;

            MakeText(rt, "Title", "MISSION", 25, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Color(0.9f, 0.7f, 0.3f), new Vector2(-300f, 180f), new Vector2(300f, 40f));
            director.narrationText = MakeText(rt, "Narration", "", 20, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.9f, 0.94f, 1f),
                new Vector2(0f, 25f), new Vector2(810f, 230f));

            director.startButton = MissionButton(rt, "Btn_Start", "START MISSION",
                new Vector2(0f, -165f), 300f, director.StartMission);
            director.continueButton = MissionButton(rt, "Btn_Continue", "CONTINUE",
                new Vector2(0f, -165f), 300f, director.ContinueMission);
            director.labButton = MissionButton(rt, "Btn_WaveLab", "ENTER WAVE LAB",
                new Vector2(0f, -165f), 300f, director.GoToWaveLab);
            director.continueButton.gameObject.SetActive(false);
            director.labButton.gameObject.SetActive(false);
            MissionButton(rt, "Btn_SkipLab", "WAVE LAB  ->", new Vector2(295f, -198f), 210f,
                director.GoToWaveLab);

            // ask Rick: question input wired to the real LLM/local backend
            var chat = canvasGO.AddComponent<TutorChatUI>();
            chat.tutor = tutor;
            chat.answerText = director.narrationText;
            chat.questionInput = MakeInput(rt, "AskInput", new Vector2(-140f, -112f),
                400f, "Ask Rick a question...");
            MakeButton(rt, "Btn_Ask", "ASK RICK", new Vector2(230f, -112f), 190f, chat.Ask);
            UnityEventTools.AddPersistentListener(tutor.onAnswer, chat.ShowAnswer);
            return director;
        }

        static Button MissionButton(Transform rt, string name, string label, Vector2 pos,
                                    float w, UnityAction call)
        {
            var t = MakeButton(rt, name, label, pos, w, call);
            return t.transform.parent.GetComponent<Button>();
        }

        // -------------------------------------------------- wave lab panel (merged controls)
        static SimulationUI BuildWaveLabPanel(Transform ship, Vector3 eye, SimulationController sim,
                                      GravitationalWaveModel model, SimulationParameters parameters,
                                      PerspectiveController perspective, PredictionTask task,
                                      AITutor tutor)
        {
            var libGO = new GameObject("WaveLibrary");
            var lib = libGO.AddComponent<WaveLibrary>();
            lib.parameters = parameters;
            lib.simulation = sim;

            var canvasGO = new GameObject("ControlPanel");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<GraphicRaycaster>();
            AddComponentByNames(canvasGO,
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            var rt = (RectTransform)canvasGO.transform;
            rt.sizeDelta = new Vector2(950f, 980f);
            canvasGO.transform.SetParent(ship, true);
            canvasGO.transform.localPosition = new Vector3(0.12f, 1.44f, 1.14f);
            canvasGO.transform.localScale = Vector3.one * 0.00115f;
            canvasGO.transform.rotation =
                Quaternion.LookRotation((canvasGO.transform.position - eye).normalized, Vector3.up);

            var bg = NewUIObject("Background", rt);
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = _uiSprite;
            bgImg.color = new Color(0.03f, 0.05f, 0.10f, 0.78f);
            bgImg.type = Image.Type.Sliced;

            var ui = canvasGO.AddComponent<SimulationUI>();
            ui.simulation = sim;
            ui.model = model;
            ui.parameters = parameters;
            ui.perspective = perspective;

            MakeButton(rt, "Btn_CloseControls", "CLOSE  X",
                new Vector2(370f, 420f), 140f, ship.GetComponent<CockpitControls>().TogglePanel);
            MakeText(rt, "Title", "WAVE LAB  -  shape it, save it, replay it on Earth",
                22, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.9f, 0.7f, 0.3f),
                new Vector2(-30f, 415f), new Vector2(800f, 40f));

            // transport row
            ui.playPauseButtonText = MakeButton(rt, "Btn_PlayPause", "Pause",
                new Vector2(-330f, 335f), 150f, ui.OnPlayPause);
            MakeButton(rt, "Btn_Slow", "0.25x", new Vector2(-160f, 335f), 110f, ui.OnSpeedQuarter);
            MakeButton(rt, "Btn_Normal", "1x", new Vector2(-30f, 335f), 90f, ui.OnSpeedNormal);
            MakeButton(rt, "Btn_Replay", "Replay", new Vector2(90f, 335f), 110f, ui.OnReplay);
            MakeButton(rt, "Btn_Reset", "Reset", new Vector2(220f, 335f), 100f, ui.OnReset);
            ui.perspectiveButtonText = MakeButton(rt, "Btn_Ride", "Ride with the wave",
                new Vector2(-160f, 270f), 260f, ui.OnTogglePerspective);
            MakeButton(rt, "Btn_Predict", "Predict next mass",
                new Vector2(130f, 270f), 220f, task.BeginPrediction);

            // sliders: amplitude, frequency, exaggeration
            ui.amplitudeSlider = MakeSlider(rt, "Slider_Amplitude",
                new Vector2(-60f, 190f), 460f, 0f, 1f, parameters.strainAmplitude, ui.SetAmplitude);
            ui.amplitudeValueText = MakeText(rt, "AmpValue", "", 16, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f),
                new Vector2(215f, 190f), new Vector2(250f, 30f));
            MakeText(rt, "AmpLabel", "strain amplitude h0", 15, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.75f, 0.75f, 0.8f),
                new Vector2(-60f, 152f), new Vector2(460f, 22f));

            ui.frequencySlider = MakeSlider(rt, "Slider_Frequency",
                new Vector2(-60f, 100f), 460f, 0.05f, 4f, parameters.frequencyHz, ui.SetFrequency);
            ui.frequencyValueText = MakeText(rt, "FreqValue", "", 16, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f),
                new Vector2(215f, 100f), new Vector2(250f, 30f));
            MakeText(rt, "FreqLabel", "frequency f (Hz)", 15, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.75f, 0.75f, 0.8f),
                new Vector2(-60f, 62f), new Vector2(460f, 22f));

            ui.exaggerationSlider = MakeSlider(rt, "Slider_Exaggeration",
                new Vector2(-60f, 10f), 460f, 0f, 2f, parameters.displacementExaggeration,
                ui.SetExaggeration);
            ui.exaggerationValueText = MakeText(rt, "ExagValue", "", 16, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f),
                new Vector2(215f, 10f), new Vector2(260f, 30f));
            MakeText(rt, "ExagLabel", "exaggeration: 0 = real scale (invisible)  ->  2 = max",
                14, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.75f, 0.75f, 0.8f),
                new Vector2(-60f, -28f), new Vector2(700f, 26f));

            // saved-wave slots
            lib.slotTexts = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                var t = MakeButton(rt, "Btn_Wave" + i, $"Wave {i + 1}  (empty)",
                    new Vector2(-300f + i * 170f, -115f), 155f);
                var b = t.transform.parent.GetComponent<Button>();
                int slot = i;
                UnityEventTools.AddIntPersistentListener(b.onClick, lib.LoadSlot, slot);
                lib.slotTexts[i] = t;
            }
            MakeText(rt, "SlotHint", "click a slot: empty = save current wave, saved = load it",
                13, FontStyle.Italic, TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.78f),
                new Vector2(0f, -160f), new Vector2(860f, 24f));

            MakeButton(rt, "Btn_Save", "Save wave", new Vector2(-130f, -230f), 210f,
                lib.SaveCurrent);
            MakeButton(rt, "Btn_ReplayWave", "Replay wave", new Vector2(130f, -230f), 210f,
                lib.ReplayCurrent);
            MakeText(rt, "IfNote",
                "the red L-frame by the ship is a LIGO-style interferometer - watch its laser arms",
                13, FontStyle.Italic, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.55f),
                new Vector2(0f, -290f), new Vector2(860f, 30f));

            // ask Rick: question input wired to the real LLM/local backend
            var chat = canvasGO.AddComponent<TutorChatUI>();
            chat.tutor = tutor;
            chat.answerText = MakeText(rt, "TutorAnswer", "", 15, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.85f, 0.92f, 1f),
                new Vector2(0f, -395f), new Vector2(880f, 44f));
            chat.questionInput = MakeInput(rt, "AskInput", new Vector2(-190f, -340f),
                380f, "Ask Rick a question...");
            MakeButton(rt, "Btn_Ask", "ASK RICK", new Vector2(140f, -340f), 190f, chat.Ask);
            UnityEventTools.AddPersistentListener(tutor.onAnswer, chat.ShowAnswer);

            ui.statusText = MakeText(rt, "Status", "", 15, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Color(0.65f, 0.9f, 0.7f),
                new Vector2(0f, -448f), new Vector2(920f, 26f));
            MakeText(rt, "Legend",
                "orange tint = stretch   |   blue tint = squeeze   |   yellow ring = wavefront   |   green/red = ahead/behind",
                13, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.65f, 0.65f, 0.72f),
                new Vector2(0f, -476f), new Vector2(920f, 20f));
            return ui;
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

            binary.blackHoleA = MakeBlackHole(mats, "BH_A", new Vector3(-5.5f, 0f, 0f), root.transform, 1.9f);
            binary.blackHoleB = MakeBlackHole(mats, "BH_B", new Vector3(5.5f, 0f, 0f), root.transform, 1.9f);
            binary.discA = binary.blackHoleA.Find("AccretionDisc");
            binary.discB = binary.blackHoleB.Find("AccretionDisc");
            binary.mergedHole = MakeBlackHole(mats, "MergedHole", Vector3.zero, root.transform, 2.4f);

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
                Debug.Log($"[Project8] Device simulator instantiated: {name}");
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

        // ------------------------------------------------------------------ markers
        static GameObject MakeRingMarker(string name, Material mat, float radius, string label,
                                         AudioClip hum = null, GravitationalWaveModel model = null,
                                         SimulationController sim = null)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 64;
            lr.widthMultiplier = 0.02f;
            lr.material = mat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            for (int i = 0; i < 64; i++)
            {
                float a = i * Mathf.PI * 2f / 64f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
            }

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            labelGO.transform.localPosition = new Vector3(0f, radius + 0.15f, 0f);
            var tm = labelGO.AddComponent<TextMesh>();
            tm.text = label;
            tm.font = _font;
            tm.fontSize = 48;
            tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = mat.color;
            labelGO.GetComponent<MeshRenderer>().sharedMaterial = _font != null ? _font.material : mat;
            labelGO.AddComponent<Project8.UI.Billboard>();
            labelGO.SetActive(false);

            if (hum != null)
            {
                var src = go.AddComponent<AudioSource>();
                src.clip = hum;
                src.loop = true;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.maxDistance = 40f;
                src.volume = 0f;
                src.playOnAwake = true;
                var cue = go.AddComponent<WavefrontAudio>();
                cue.model = model;
                cue.simulation = sim;
            }
            return go;
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

        // ------------------------------------------------------------------ UI panel
        static SimulationUI BuildControlPanel(SimulationController sim, GravitationalWaveModel model,
                                              SimulationParameters parameters,
                                              PerspectiveController perspective, PredictionTask task,
                                              Transform shipRoot, Vector3 pos, Vector3 eye)
        {
            var canvasGO = new GameObject("ControlPanel");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<GraphicRaycaster>();
            // lets XRI poke/ray interactors press the widgets (mouse still works in editor)
            AddComponentByNames(canvasGO,
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            var rt = (RectTransform)canvasGO.transform;
            rt.sizeDelta = new Vector2(950f, 850f);
            canvasGO.transform.SetParent(shipRoot, true);
            canvasGO.transform.position = pos;
            canvasGO.transform.localScale = Vector3.one * 0.00115f;
            // dashboard tilt: face the seated eye position
            canvasGO.transform.rotation = Quaternion.LookRotation((pos - eye).normalized, Vector3.up);

            var bg = NewUIObject("Background", rt);
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = _uiSprite;
            bgImg.color = new Color(0.04f, 0.05f, 0.09f, 0.78f);
            bgImg.type = Image.Type.Sliced;

            var ui = canvasGO.AddComponent<SimulationUI>();
            ui.simulation = sim;
            ui.model = model;
            ui.parameters = parameters;
            ui.perspective = perspective;

            MakeButton(rt, "Btn_CloseControls", "CLOSE  X",
                new Vector2(370f, 380f), 140f, shipRoot.GetComponent<CockpitControls>().TogglePanel);

            // header
            var title = MakeText(rt, "Title", "PROJECT 08  -  SURFING A GRAVITATIONAL WAVE",
                26, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, 335f), new Vector2(920f, 42f));
            MakeText(rt, "Disclaimer",
                "Simplified teaching model: deformation is EXAGGERATED and propagation is SLOWED - " +
                "real gravitational waves move at the speed of light.",
                15, FontStyle.Italic, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f),
                new Vector2(0f, 270f), new Vector2(900f, 58f));

            // row 1: transport controls
            ui.playPauseButtonText = MakeButton(rt, "Btn_PlayPause", "Pause",
                new Vector2(-340f, 175f), 150f, ui.OnPlayPause);
            MakeButton(rt, "Btn_Slow", "0.25x", new Vector2(-170f, 175f), 110f, ui.OnSpeedQuarter);
            MakeButton(rt, "Btn_Normal", "1x", new Vector2(-40f, 175f), 90f, ui.OnSpeedNormal);
            MakeButton(rt, "Btn_Replay", "Replay", new Vector2(80f, 175f), 110f, ui.OnReplay);
            MakeButton(rt, "Btn_Reset", "Reset", new Vector2(210f, 175f), 100f, ui.OnReset);

            // row 2: perspective + prediction
            ui.perspectiveButtonText = MakeButton(rt, "Btn_Ride", "Ride with the wave",
                new Vector2(-170f, 100f), 260f, ui.OnTogglePerspective);
            MakeButton(rt, "Btn_Predict", "Predict next mass",
                new Vector2(120f, 100f), 220f, task.BeginPrediction);

            // sliders
            ui.amplitudeSlider = MakeSlider(rt, "Slider_Amplitude",
                new Vector2(-60f, 10f), 460f, 0f, 1f, parameters.strainAmplitude, ui.SetAmplitude);
            ui.amplitudeValueText = MakeText(rt, "AmpValue", "", 16, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f),
                new Vector2(215f, 10f), new Vector2(250f, 30f));
            MakeText(rt, "AmpLabel", "strain amplitude h0", 15, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.75f, 0.75f, 0.8f),
                new Vector2(-60f, -25f), new Vector2(460f, 22f));

            ui.frequencySlider = MakeSlider(rt, "Slider_Frequency",
                new Vector2(-60f, -105f), 460f, 0.05f, 2f, parameters.frequencyHz, ui.SetFrequency);
            ui.frequencyValueText = MakeText(rt, "FreqValue", "", 16, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f),
                new Vector2(215f, -105f), new Vector2(250f, 30f));
            MakeText(rt, "FreqLabel", "frequency f (Hz)", 15, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.75f, 0.75f, 0.8f),
                new Vector2(-60f, -140f), new Vector2(460f, 22f));

            // status line
            ui.statusText = MakeText(rt, "Status", "", 15, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Color(0.65f, 0.9f, 0.7f),
                new Vector2(0f, -285f), new Vector2(920f, 30f));

            // legend
            MakeText(rt, "Legend",
                "orange tint = stretch   |   blue tint = squeeze   |   yellow ring = wavefront   |   green/red = ahead/behind",
                13, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.65f, 0.65f, 0.72f),
                new Vector2(0f, -365f), new Vector2(920f, 24f));

            return ui;
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
                case "PROJECT 08  -  SURFING A GRAVITATIONAL WAVE": return "项目 08  -  冲浪引力波";
                case "START MISSION": return "开始任务";
                case "CONTINUE": return "继续";
                case "ENTER WAVE LAB": return "进入波形实验室";
                case "WAVE LAB  ->": return "波形实验室  ->";
                case "CLOSE  X": return "收起  X";
                case "Pause": return "暂停";
                case "Replay": return "重播";
                case "Reset": return "重置";
                case "Ride with the wave": return "跟随引力波";
                case "Predict next mass": return "预测下一个小球";
                case "ASK RICK": return "提问 RICK";
                case "strain amplitude h0": return "应变振幅 h0";
                case "frequency f (Hz)": return "频率 f（赫兹）";
                case "WAVE LAB  -  shape it, save it, replay it on Earth": return "波形实验室  -  调整、保存、回放";
                case "Save wave": return "保存波形";
                case "Replay wave": return "回放波形";
                case "click a slot: empty = save current wave, saved = load it": return "点击空槽保存波形，点击已存槽加载波形";
                case "exaggeration: 0 = real scale (invisible)  ->  2 = max": return "形变夸张度：0 = 实际尺度（不可见），2 = 最大";
                case "the red L-frame by the ship is a LIGO-style interferometer - watch its laser arms":
                    return "右侧红色 L 形结构是干涉仪示意：观察激光臂长度变化";
                default:
                    if (english.StartsWith("Wave ") && english.EndsWith("  (empty)"))
                        return "波形 " + english.Substring(5, 1) + "（空）";
                    if (english.StartsWith("Simplified teaching model:"))
                        return "简化教学模型：形变经过夸张，传播速度被放慢；真实引力波以光速传播。";
                    if (english.StartsWith("orange tint ="))
                        return "橙色=拉伸  |  蓝色=挤压  |  黄色圆环=波前  |  绿/红=前方/后方";
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
                case "PROJECT 08  -  SURFING A GRAVITATIONAL WAVE":
                    return "PROJECT 08  -  SURFEN OP EEN ZWAARTEKRACHTGOLF";
                case "START MISSION": return "MISSIE STARTEN";
                case "CONTINUE": return "DOORGAAN";
                case "ENTER WAVE LAB": return "NAAR HET GOLVENLAB";
                case "WAVE LAB  ->": return "GOLVENLAB  ->";
                case "CLOSE  X": return "SLUITEN  X";
                case "Pause": return "Pauze";
                case "Replay": return "Opnieuw";
                case "Reset": return "Resetten";
                case "Ride with the wave": return "Surfen op de golf";
                case "Predict next mass": return "Voorspel volgende massa";
                case "ASK RICK": return "VRAAG AAN RICK";
                case "strain amplitude h0": return "rek-amplitude h0";
                case "frequency f (Hz)": return "frequentie f (Hz)";
                case "WAVE LAB  -  shape it, save it, replay it on Earth":
                    return "GOLVENLAB  -  vorm het, sla het op, speel het af op de Aarde";
                case "Save wave": return "Golf opslaan";
                case "Replay wave": return "Golf afspelen";
                case "click a slot: empty = save current wave, saved = load it":
                    return "klik op een slot: leeg = huidige golf opslaan, opgeslagen = laden";
                case "exaggeration: 0 = real scale (invisible)  ->  2 = max":
                    return "overdrijving: 0 = echte schaal (onzichtbaar)  ->  2 = max";
                case "the red L-frame by the ship is a LIGO-style interferometer - watch its laser arms":
                    return "het rode L-frame bij het schip is een LIGO-achtige interferometer - let op de laserarmen";
                default:
                    if (english.StartsWith("Wave ") && english.EndsWith("  (empty)"))
                        return "Golf " + english.Substring(5, 1) + "  (leeg)";
                    if (english.StartsWith("Simplified teaching model:"))
                        return "Vereenvoudigd lesmodel: deformatie is OVERDREVEN en de voortplanting " +
                               "is VERTRAAGD - echte zwaartekrachtgolven gaan met lichtsnelheid.";
                    if (english.StartsWith("orange tint ="))
                        return "oranje = uitrekken  |  blauw = samentrekken  |  gele ring = golffront  |  groen/rood = voor/achter";
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
            rt.sizeDelta = new Vector2(width, 40f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = _uiSprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.18f, 0.32f, 0.55f, 0.95f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.55f, 0.85f);
            colors.pressedColor = new Color(0.6f, 0.75f, 1f);
            btn.colors = colors;
            if (onClick != null) UnityEventTools.AddPersistentListener(btn.onClick, onClick);

            var text = MakeText(rt, "Label", label, 16, FontStyle.Bold, TextAnchor.MiddleCenter,
                Color.white, Vector2.zero, rt.sizeDelta);
            Stretch((RectTransform)text.transform);
            return text;
        }

        static InputField MakeInput(Transform parent, string name, Vector2 pos, float width,
                                    string hint)
        {
            var rt = NewUIObject(name, parent);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 42f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = _uiSprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.06f, 0.08f, 0.14f, 0.95f);
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

            var bg = NewUIObject("Background", rt);
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = _uiSprite;
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(0.1f, 0.12f, 0.18f, 1f);

            var fillArea = NewUIObject("Fill Area", rt);
            Stretch(fillArea);
            var fill = NewUIObject("Fill", fillArea);
            Stretch(fill);
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.sprite = _uiSprite;
            fillImg.type = Image.Type.Sliced;
            fillImg.color = new Color(0.3f, 0.6f, 0.9f, 1f);
            slider.fillRect = fill;

            var handleArea = NewUIObject("Handle Slide Area", rt);
            Stretch(handleArea);
            var handle = NewUIObject("Handle", handleArea);
            handle.sizeDelta = new Vector2(34f, 40f);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.sprite = _knobSprite != null ? _knobSprite : _uiSprite;
            handleImg.color = Color.white;
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
