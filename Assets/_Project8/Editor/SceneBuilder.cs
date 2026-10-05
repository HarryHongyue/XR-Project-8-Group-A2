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
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene($"{Root}/Scenes/Main.unity", true),
                    new EditorBuildSettingsScene($"{Root}/Scenes/WaveLab.unity", true),
                };
                ConfigureOpenXRLoader();
                PlayerSettings.colorSpace = ColorSpace.Linear;
                PlayerSettings.productName = "Project 08 - Surfing a Gravitational Wave";
                AssetDatabase.SaveAssets();
                Debug.Log("[Project8] Scene build finished successfully.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Project8] Build failed: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
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
            public Material NetLine, BlackHole, Accretion, Planet, StarGlow, Laser;
        }

        static Mats CreateMaterials()
        {
            var m = new Mats();
            m.TestMass = MakeMaterial("M_TestMass", "Standard", new Color(0.92f, 0.92f, 0.95f), 0.35f);
            m.TestMass.EnableKeyword("_EMISSION");
            m.TestMass.SetColor("_EmissionColor", Color.black);
            m.Handle = MakeMaterial("M_StrainHandle", "Standard", new Color(0.15f, 0.7f, 0.75f), 0.5f);
            m.Handle.EnableKeyword("_EMISSION");
            m.Handle.SetColor("_EmissionColor", new Color(0.02f, 0.25f, 0.28f));
            m.MarkerFront = MakeMaterial("M_MarkerFront", "Sprites/Default", new Color(1f, 0.85f, 0.1f, 0.9f));
            m.MarkerAhead = MakeMaterial("M_MarkerAhead", "Sprites/Default", new Color(0.25f, 0.9f, 0.35f, 0.65f));
            m.MarkerBehind = MakeMaterial("M_MarkerBehind", "Sprites/Default", new Color(0.95f, 0.25f, 0.25f, 0.65f));

            m.Hull = MakeMaterial("M_Hull", "Standard", new Color(0.55f, 0.57f, 0.63f), 0.55f);
            m.HullDark = MakeMaterial("M_HullDark", "Standard", new Color(0.10f, 0.11f, 0.14f), 0.35f);
            m.Console = MakeMaterial("M_Console", "Standard", new Color(0.16f, 0.17f, 0.21f), 0.5f);
            m.Glass = MakeMaterial("M_Glass", "Standard", new Color(0.6f, 0.8f, 0.95f, 0.07f), 0.95f);
            SetupTransparent(m.Glass);
            m.EngineGlow = MakeMaterial("M_EngineGlow", "Standard", new Color(0.15f, 0.85f, 0.9f), 0.6f);
            m.EngineGlow.EnableKeyword("_EMISSION");
            m.EngineGlow.SetColor("_EmissionColor", new Color(0.1f, 1.5f, 1.6f));

            m.NetLine = MakeMaterial("M_NetLine", "Legacy Shaders/Particles/Additive", Color.white);
            m.NetLine.SetColor("_TintColor", new Color(0.3f, 0.8f, 1f, 0.45f));
            m.BlackHole = MakeMaterial("M_BlackHole", "Standard", new Color(0.012f, 0.008f, 0.016f), 0.98f);
            m.Accretion = MakeMaterial("M_Accretion", "Standard", new Color(0.9f, 0.45f, 0.1f), 0.4f);
            m.Accretion.EnableKeyword("_EMISSION");
            m.Accretion.SetColor("_EmissionColor", new Color(2.4f, 1.0f, 0.28f));
            m.Planet = MakeMaterial("M_Planet", "Standard", new Color(0.32f, 0.46f, 0.72f), 0.72f);
            m.StarGlow = MakeMaterial("M_Star", "Standard", new Color(1f, 0.95f, 0.8f), 0f);
            m.StarGlow.EnableKeyword("_EMISSION");
            m.StarGlow.SetColor("_EmissionColor", new Color(3f, 2.6f, 2f));
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

        static Material MakeMaterial(string name, string shader, Color color, float smooth = 0f)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = Shader.Find(shader);
            mat.color = color;
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smooth);
            return mat;
        }

        static TestMass CreateTestMassPrefab(Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "TestMass";
            go.transform.localScale = Vector3.one * 0.14f;
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
            p.ringdownSeconds = 2.5f;
            p.visualizationSpeedMps = 2.2f;
            p.displacementExaggeration = 1f;
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
        static void BuildScene(Mats mats, TestMass massPrefab, SimulationParameters parameters,
                               TutorKnowledgeBase knowledge, AudioClip hum, bool story, string sceneName)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Vector3 seatPos = new Vector3(0f, 0f, -0.15f);
            Vector3 eye = seatPos + new Vector3(0f, 1.45f, 0f);
            Vector3 binaryPos = new Vector3(0f, 4f, 35f);
            Vector3 netPos = new Vector3(0f, 2.5f, 12f);
            Vector3 earthPos = new Vector3(0f, -4f, -24f);

            // --- space environment --------------------------------------------
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.09f, 0.11f, 0.16f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;

            var starGO = new GameObject("Starlight");
            var starLight = starGO.AddComponent<Light>();
            starLight.type = LightType.Directional;
            starLight.intensity = 0.85f;
            starLight.color = new Color(0.85f, 0.9f, 1f);
            starLight.shadows = LightShadows.Soft;
            starGO.transform.rotation = Quaternion.Euler(35f, -130f, 0f);

            BuildStarfield();
            BuildNebulae();
            BuildPlanetAndStar(mats);

            // --- simulation core ----------------------------------------------
            var simGO = new GameObject("Simulation");
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
            if (story)
            {
                BuildBinaryBlackHoles(mats, controller, binaryPos,
                    parameters.inspiralDurationSeconds);
            }
            else
            {
                MakeBlackHole(mats, "RemnantHole", binaryPos, simGO.transform, 1.7f);
            }

            // --- Earth (both scenes: story wave hits it, lab replays on it) ------
            var earth = BuildEarth(mats, earthPos, 7f);
            var deformer = earth.GetComponent<EarthDeformer>();
            deformer.model = model;
            deformer.simulation = controller;

            // --- test-mass "net" --------------------------------------------------
            var fieldGO = new GameObject("TestMassNet");
            fieldGO.transform.position = netPos;
            var field = fieldGO.AddComponent<TestMassField>();
            field.model = model;
            field.simulation = controller;
            field.testMassPrefab = massPrefab;
            field.layout = TestMassField.FieldLayout.Grid;
            field.gridSize = 9;
            field.gridSpacing = 0.7f;
            var net = fieldGO.AddComponent<LatticeNet>();
            net.field = field;
            net.lineMaterial = mats.NetLine;

            // --- wavefront markers ---------------------------------------------
            var markersGO = new GameObject("WavefrontMarkers");
            markersGO.transform.SetParent(simGO.transform, false);
            var markers = markersGO.AddComponent<WavefrontMarkers>();
            markers.model = model;
            markers.simulation = controller;
            markers.maxVisibleDistance = 60f;
            markers.markerOffset = 1.5f;
            markers.wavefrontMarker = MakeRingMarker("Marker_Wavefront", mats.MarkerFront,
                3.2f, "wavefront (where the disturbance has reached)", hum,
                model, controller).transform;
            markers.aheadMarker = MakeRingMarker("Marker_Ahead", mats.MarkerAhead,
                3.2f, "ahead of the wavefront").transform;
            markers.behindMarker = MakeRingMarker("Marker_Behind", mats.MarkerBehind,
                3.2f, "behind the wavefront").transform;

            // --- spaceship + seated rig -----------------------------------------
            var ship = BuildSpaceship(mats, hum);
            var rig = InstantiateXRRig();
            rig.transform.SetParent(ship.transform, false);
            rig.transform.localPosition = seatPos;
            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.008f, 0.01f, 0.02f);
            }
            SetupInteractionManager();
            InstantiateDeviceSimulator();

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
            var panel = BuildControlPanel(controller, model, parameters, perspective, task,
                ship.transform, new Vector3(0f, 1.04f, 0.60f), eye);
            task.ui = panel;

            // --- scene-specific extras ---------------------------------------------
            if (story)
            {
                BuildMissionPanel(ship.transform, eye, controller, model, earth.transform);
            }
            else
            {
                BuildWaveLabPanel(ship.transform, eye, controller, parameters, panel);
                BuildInterferometer(mats, model, controller, new Vector3(3.4f, 1.35f, 5f));
            }

            // --- grabbable strain handle on the left console wing --------------------
            var anchor = new GameObject("StrainHandle_Anchor");
            anchor.transform.SetParent(ship.transform, false);
            anchor.transform.localPosition = new Vector3(-0.46f, 0.98f, 0.52f);
            var handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handle.name = "StrainHandle (grab me to change amplitude)";
            handle.transform.SetParent(ship.transform, false);
            handle.transform.localPosition = anchor.transform.localPosition;
            handle.transform.localScale = Vector3.one * 0.11f;
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
            MakeFloatingLabel("grab me:\namplitude h0", ship.transform,
                new Vector3(-0.46f, 1.18f, 0.52f), 0.05f, new Color(0.6f, 0.95f, 0.95f), eye);

            // --- world labels ---------------------------------------------------------
            MakeFloatingLabel(
                story ? "binary black holes - merger emits gravitational waves"
                      : "post-merger remnant - shape and replay the wave from the lab",
                simGO.transform, binaryPos + new Vector3(0f, 3.6f, 0f), 0.11f,
                new Color(1f, 0.7f, 0.35f), eye);
            MakeFloatingLabel(
                "test-mass net - watches spacetime stretch and squeeze\n" +
                "deformation EXAGGERATED, propagation SLOWED (real GWs move at c)",
                simGO.transform, netPos + new Vector3(0f, 3.4f, 0f), 0.055f,
                new Color(1f, 0.9f, 0.4f), eye);
            MakeFloatingLabel("Earth", simGO.transform,
                earthPos + new Vector3(0f, 8.6f, 0f), 0.12f, new Color(0.6f, 0.85f, 1f), eye);
            if (!story)
            {
                MakeFloatingLabel("LIGO-style interferometer:\nthe laser arms change length with the passing wave",
                    simGO.transform, new Vector3(3.4f, 2.5f, 5f), 0.045f,
                    new Color(1f, 0.5f, 0.5f), eye);
            }

            // --- event system ---------------------------------------------------------
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            AddComponentByNames(es,
                "UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit");
            es.AddComponent<StandaloneInputModule>(); // mouse works in the Editor too

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
            e.GetComponent<Renderer>().sharedMaterial = mats.Planet;
            KillCollider(e);
            var d = e.AddComponent<EarthDeformer>();
            d.visualGain = 0.35f;
            return e.transform;
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

        // ------------------------------------------------------------- mission panel (story)
        static void BuildMissionPanel(Transform ship, Vector3 eye, SimulationController sim,
                                      GravitationalWaveModel model, Transform earth)
        {
            var dirGO = new GameObject("StoryDirector");
            var director = dirGO.AddComponent<StoryDirector>();
            director.simulation = sim;
            director.model = model;
            director.ship = ship;
            director.earth = earth;
            director.flyTarget = new Vector3(0f, 0.9f, 7f);

            var canvasGO = new GameObject("MissionPanel");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<GraphicRaycaster>();
            AddComponentByNames(canvasGO,
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            var rt = (RectTransform)canvasGO.transform;
            rt.sizeDelta = new Vector2(900f, 300f);
            canvasGO.transform.SetParent(ship, true);
            canvasGO.transform.position = new Vector3(0f, 0.62f, 0.63f);
            canvasGO.transform.localScale = Vector3.one * 0.00085f;
            canvasGO.transform.rotation =
                Quaternion.LookRotation((eye - canvasGO.transform.position).normalized, Vector3.up);

            var bg = NewUIObject("Background", rt);
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = _uiSprite;
            bgImg.color = new Color(0.03f, 0.05f, 0.10f, 0.9f);
            bgImg.type = Image.Type.Sliced;

            MakeText(rt, "Title", "MISSION", 20, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Color(0.9f, 0.7f, 0.3f), new Vector2(-420f, -18f), new Vector2(200f, 26f));
            director.narrationText = MakeText(rt, "Narration", "", 15, FontStyle.Normal,
                TextAnchor.UpperLeft, new Color(0.9f, 0.94f, 1f),
                new Vector2(0f, -95f), new Vector2(850f, 130f));

            director.startButton = MissionButton(rt, "Btn_Start", "START MISSION",
                new Vector2(-290f, -215f), 220f, director.StartMission);
            director.continueButton = MissionButton(rt, "Btn_Continue", "CONTINUE",
                new Vector2(-30f, -215f), 180f, director.ContinueMission);
            director.labButton = MissionButton(rt, "Btn_WaveLab", "ENTER WAVE LAB",
                new Vector2(240f, -215f), 240f, director.GoToWaveLab);
        }

        static Button MissionButton(Transform rt, string name, string label, Vector2 pos,
                                    float w, UnityAction call)
        {
            var t = MakeButton(rt, name, label, pos, w, call);
            return t.transform.parent.GetComponent<Button>();
        }

        // ------------------------------------------------------------- wave lab panel
        static void BuildWaveLabPanel(Transform ship, Vector3 eye, SimulationController sim,
                                      SimulationParameters parameters, SimulationUI ui)
        {
            var libGO = new GameObject("WaveLibrary");
            var lib = libGO.AddComponent<WaveLibrary>();
            lib.parameters = parameters;
            lib.simulation = sim;

            var canvasGO = new GameObject("WaveLabPanel");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<GraphicRaycaster>();
            AddComponentByNames(canvasGO,
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            var rt = (RectTransform)canvasGO.transform;
            rt.sizeDelta = new Vector2(900f, 360f);
            canvasGO.transform.SetParent(ship, true);
            canvasGO.transform.position = new Vector3(0f, 0.60f, 0.63f);
            canvasGO.transform.localScale = Vector3.one * 0.00085f;
            canvasGO.transform.rotation =
                Quaternion.LookRotation((eye - canvasGO.transform.position).normalized, Vector3.up);

            var bg = NewUIObject("Background", rt);
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = _uiSprite;
            bgImg.color = new Color(0.03f, 0.05f, 0.10f, 0.9f);
            bgImg.type = Image.Type.Sliced;

            MakeText(rt, "Title", "WAVE LAB  -  shape it, save it, replay it on Earth",
                19, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.9f, 0.7f, 0.3f),
                new Vector2(-430f, -20f), new Vector2(860f, 28f));

            ui.exaggerationSlider = MakeSlider(rt, "Slider_Exaggeration",
                new Vector2(-60f, -75f), 460f, 0f, 2f, parameters.displacementExaggeration,
                ui.SetExaggeration);
            ui.exaggerationValueText = MakeText(rt, "ExagValue", "", 16, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f),
                new Vector2(215f, -75f), new Vector2(260f, 30f));
            MakeText(rt, "ExagLabel", "exaggeration: 0 = real scale (invisible)  ->  2 = max",
                14, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.75f, 0.75f, 0.8f),
                new Vector2(-60f, -102f), new Vector2(700f, 22f));

            // saved-wave slots
            lib.slotTexts = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                var t = MakeButton(rt, "Btn_Wave" + i, $"Wave {i + 1}  (empty)",
                    new Vector2(-300f + i * 170f, -160f), 155f);
                var b = t.transform.parent.GetComponent<Button>();
                int slot = i;
                UnityEventTools.AddIntPersistentListener(b.onClick, lib.LoadSlot, slot);
                lib.slotTexts[i] = t;
            }
            MakeText(rt, "SlotHint", "click a slot: empty = save current wave, saved = load it",
                13, FontStyle.Italic, TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.78f),
                new Vector2(0f, -195f), new Vector2(860f, 20f));

            MakeButton(rt, "Btn_Save", "Save wave", new Vector2(-220f, -255f), 180f,
                lib.SaveCurrent);
            MakeButton(rt, "Btn_ReplayWave", "Replay wave", new Vector2(0f, -255f), 180f,
                lib.ReplayCurrent);
            MakeText(rt, "IfNote",
                "the red L-frame by the ship is a LIGO-style interferometer - watch its laser arms",
                13, FontStyle.Italic, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.55f),
                new Vector2(0f, -315f), new Vector2(860f, 22f));
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

            var console = GameObject.CreatePrimitive(PrimitiveType.Cube);
            console.name = "Console";
            console.transform.SetParent(ship.transform, false);
            console.transform.localPosition = new Vector3(0f, 0.9f, 0.6f);
            console.transform.localRotation = Quaternion.Euler(-56f, 0f, 0f);
            console.transform.localScale = new Vector3(1.15f, 0.05f, 0.66f);
            console.GetComponent<Renderer>().sharedMaterial = mats.Console;

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
        static void BuildStarfield()
        {
            var go = new GameObject("Starfield");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1e5f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.7f);
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
        }

        static void BuildNebulae()
        {
            var tex = CreateNebulaTexture();
            var defs = new (Vector3 pos, Color col, float size)[]
            {
                (new Vector3(-95f, 45f, 90f),   new Color(0.5f, 0.2f, 0.7f, 0.32f),  120f),
                (new Vector3(85f, -35f, 75f),   new Color(0.15f, 0.45f, 0.6f, 0.28f), 95f),
                (new Vector3(-40f, 15f, -120f), new Color(0.7f, 0.3f, 0.2f, 0.22f),  140f),
                (new Vector3(60f, 55f, -70f),   new Color(0.3f, 0.35f, 0.75f, 0.2f),  90f),
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

            binary.blackHoleA = MakeBlackHole(mats, "BH_A", new Vector3(-5.5f, 0f, 0f), root.transform, 1.1f);
            binary.blackHoleB = MakeBlackHole(mats, "BH_B", new Vector3(5.5f, 0f, 0f), root.transform, 1.1f);
            binary.discA = binary.blackHoleA.Find("AccretionDisc");
            binary.discB = binary.blackHoleB.Find("AccretionDisc");
            binary.mergedHole = MakeBlackHole(mats, "MergedHole", Vector3.zero, root.transform, 1.7f);

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

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "AccretionDisc";
            disc.transform.SetParent(hole.transform, false);
            disc.transform.localPosition = Vector3.zero;
            disc.transform.localScale = new Vector3(3f, 0.015f, 3f);
            disc.transform.localRotation = Quaternion.Euler(14f, 0f, 9f);
            disc.GetComponent<Renderer>().sharedMaterial = mats.Accretion;
            KillCollider(disc);

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
            int bestScore = -1;
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                int score = 0;
                if (name.IndexOf("Interaction Setup", StringComparison.OrdinalIgnoreCase) >= 0) score += 100;
                if (name.IndexOf("XR Origin", StringComparison.OrdinalIgnoreCase) >= 0) score += 50;
                if (name.IndexOf("XR", StringComparison.OrdinalIgnoreCase) >= 0) score += 10;
                if (score > bestScore) { bestScore = score; best = AssetDatabase.LoadAssetAtPath<GameObject>(path); }
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
            rt.sizeDelta = new Vector2(950f, 620f);
            canvasGO.transform.SetParent(shipRoot, true);
            canvasGO.transform.position = pos;
            canvasGO.transform.localScale = Vector3.one * 0.00085f;
            // dashboard tilt: face the seated eye position
            canvasGO.transform.rotation = Quaternion.LookRotation((eye - pos).normalized, Vector3.up);

            var bg = NewUIObject("Background", rt);
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = _uiSprite;
            bgImg.color = new Color(0.04f, 0.05f, 0.09f, 0.92f);
            bgImg.type = Image.Type.Sliced;

            var ui = canvasGO.AddComponent<SimulationUI>();
            ui.simulation = sim;
            ui.model = model;
            ui.parameters = parameters;
            ui.perspective = perspective;

            // header
            var title = MakeText(rt, "Title", "PROJECT 08  -  SURFING A GRAVITATIONAL WAVE",
                26, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, -32f), new Vector2(920f, 36f));
            MakeText(rt, "Disclaimer",
                "Simplified teaching model: deformation is EXAGGERATED and propagation is SLOWED - " +
                "real gravitational waves move at the speed of light.",
                15, FontStyle.Italic, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f),
                new Vector2(0f, -62f), new Vector2(920f, 40f));

            // row 1: transport controls
            ui.playPauseButtonText = MakeButton(rt, "Btn_PlayPause", "Pause",
                new Vector2(-340f, -115f), 150f, ui.OnPlayPause);
            MakeButton(rt, "Btn_Slow", "0.25x", new Vector2(-170f, -115f), 110f, ui.OnSpeedQuarter);
            MakeButton(rt, "Btn_Normal", "1x", new Vector2(-40f, -115f), 90f, ui.OnSpeedNormal);
            MakeButton(rt, "Btn_Replay", "Replay", new Vector2(80f, -115f), 110f, ui.OnReplay);
            MakeButton(rt, "Btn_Reset", "Reset", new Vector2(210f, -115f), 100f, ui.OnReset);

            // row 2: perspective + prediction
            ui.perspectiveButtonText = MakeButton(rt, "Btn_Ride", "Ride with the wave",
                new Vector2(-170f, -170f), 260f, ui.OnTogglePerspective);
            MakeButton(rt, "Btn_Predict", "Predict next mass",
                new Vector2(120f, -170f), 220f, task.BeginPrediction);

            // sliders
            ui.amplitudeSlider = MakeSlider(rt, "Slider_Amplitude",
                new Vector2(-60f, -245f), 460f, 0f, 1f, parameters.strainAmplitude, ui.SetAmplitude);
            ui.amplitudeValueText = MakeText(rt, "AmpValue", "", 16, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f),
                new Vector2(215f, -245f), new Vector2(250f, 30f));
            MakeText(rt, "AmpLabel", "strain amplitude h0", 15, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.75f, 0.75f, 0.8f),
                new Vector2(-60f, -272f), new Vector2(460f, 22f));

            ui.frequencySlider = MakeSlider(rt, "Slider_Frequency",
                new Vector2(-60f, -335f), 460f, 0.05f, 2f, parameters.frequencyHz, ui.SetFrequency);
            ui.frequencyValueText = MakeText(rt, "FreqValue", "", 16, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f),
                new Vector2(215f, -335f), new Vector2(250f, 30f));
            MakeText(rt, "FreqLabel", "frequency f (Hz)", 15, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Color(0.75f, 0.75f, 0.8f),
                new Vector2(-60f, -362f), new Vector2(460f, 22f));

            // status line
            ui.statusText = MakeText(rt, "Status", "", 15, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Color(0.65f, 0.9f, 0.7f),
                new Vector2(0f, -560f), new Vector2(920f, 30f));

            // legend
            MakeText(rt, "Legend",
                "orange tint = stretch   |   blue tint = squeeze   |   yellow ring = wavefront   |   green/red = ahead/behind",
                13, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.65f, 0.65f, 0.72f),
                new Vector2(0f, -590f), new Vector2(920f, 24f));

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

        static Slider MakeSlider(Transform parent, string name, Vector2 pos, float width,
                                 float min, float max, float value, UnityAction<float> onValue)
        {
            var rt = NewUIObject(name, parent);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 24f);
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
            handle.sizeDelta = new Vector2(26f, 30f);
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
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = asm.GetName().Name;
                if (n.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("XR", StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.AppendLine(n);
            }
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
