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
                var parameters = CreateParameters();
                var knowledge = CreateKnowledgeBase();
                var hum = CreateHumClip();
                BuildScene(mats, prefab, parameters, knowledge, hum);
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
        struct Mats { public Material TestMass, Ground, MarkerFront, MarkerAhead, MarkerBehind, Handle; }

        static Mats CreateMaterials()
        {
            var m = new Mats();
            m.TestMass = MakeMaterial("M_TestMass", "Standard", new Color(0.92f, 0.92f, 0.95f), 0.35f);
            m.TestMass.EnableKeyword("_EMISSION");
            m.TestMass.SetColor("_EmissionColor", Color.black);
            m.Ground = MakeMaterial("M_Ground", "Standard", new Color(0.11f, 0.12f, 0.16f), 0.15f);
            m.Handle = MakeMaterial("M_StrainHandle", "Standard", new Color(0.15f, 0.7f, 0.75f), 0.5f);
            m.Handle.EnableKeyword("_EMISSION");
            m.Handle.SetColor("_EmissionColor", new Color(0.02f, 0.25f, 0.28f));
            m.MarkerFront = MakeMaterial("M_MarkerFront", "Sprites/Default", new Color(1f, 0.85f, 0.1f, 0.9f));
            m.MarkerAhead = MakeMaterial("M_MarkerAhead", "Sprites/Default", new Color(0.25f, 0.9f, 0.35f, 0.65f));
            m.MarkerBehind = MakeMaterial("M_MarkerBehind", "Sprites/Default", new Color(0.95f, 0.25f, 0.25f, 0.65f));
            return m;
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

        static SimulationParameters CreateParameters()
        {
            string path = $"{Root}/Settings/SimulationParameters.asset";
            var p = AssetDatabase.LoadAssetAtPath<SimulationParameters>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<SimulationParameters>();
                AssetDatabase.CreateAsset(p, path);
            }
            // Teaching-model defaults (see Documentation/PhysicsModel.md)
            p.strainAmplitude = 0.35f;
            p.frequencyHz = 0.5f;
            p.phaseRadians = 0f;
            p.propagationAxis = PropagationAxis.PositiveZ;
            p.waveMode = WaveMode.Burst;
            p.packetDurationSeconds = 4f;
            p.visualizationSpeedMps = 1.5f;
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
                               TutorKnowledgeBase knowledge, AudioClip hum)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- environment --------------------------------------------------
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.24f, 0.3f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.05f, 0.06f, 0.09f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 12f;
            RenderSettings.fogEndDistance = 30f;

            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            lightGO.transform.rotation = Quaternion.Euler(50f, -140f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground (Teleport Area)";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            ground.GetComponent<Renderer>().sharedMaterial = mats.Ground;
            AddComponentByNames(ground,
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea, Unity.XR.Interaction.Toolkit",
                "UnityEngine.XR.Interaction.Toolkit.TeleportationArea, Unity.XR.Interaction.Toolkit");

            // --- simulation core ----------------------------------------------
            var simGO = new GameObject("Simulation");
            var model = simGO.AddComponent<GravitationalWaveModel>();
            model.parameters = parameters;
            var controller = simGO.AddComponent<SimulationController>();
            controller.parameters = parameters;

            var originGO = new GameObject("WaveOrigin (s = 0)");
            originGO.transform.position = new Vector3(0f, 1.5f, -1f);
            model.waveOrigin = originGO.transform;

            var loggerGO = new GameObject("ResearchLogger");
            var logger = loggerGO.AddComponent<ResearchLogger>();
            logger.simulation = controller;

            var tutorGO = new GameObject("AITutor");
            var tutor = tutorGO.AddComponent<AITutor>();
            tutor.knowledgeBase = knowledge;

            // --- test-object field ---------------------------------------------
            var fieldGO = new GameObject("TestMassField (Ring)");
            fieldGO.transform.position = new Vector3(0f, 1.5f, 3f);
            var field = fieldGO.AddComponent<TestMassField>();
            field.model = model;
            field.simulation = controller;
            field.testMassPrefab = massPrefab;
            field.layout = TestMassField.FieldLayout.Ring;
            field.ringCount = 24;
            field.ringRadius = 1.2f;

            // --- wavefront markers ---------------------------------------------
            var markersGO = new GameObject("WavefrontMarkers");
            markersGO.transform.SetParent(simGO.transform, false);
            var markers = markersGO.AddComponent<WavefrontMarkers>();
            markers.model = model;
            markers.simulation = controller;
            markers.wavefrontMarker = MakeRingMarker("Marker_Wavefront", mats.MarkerFront,
                field.ringRadius * 1.15f, "wavefront (where the disturbance has reached)", hum,
                model, controller).transform;
            markers.aheadMarker = MakeRingMarker("Marker_Ahead", mats.MarkerAhead,
                field.ringRadius * 1.15f, "ahead of the wavefront").transform;
            markers.behindMarker = MakeRingMarker("Marker_Behind", mats.MarkerBehind,
                field.ringRadius * 1.15f, "behind the wavefront").transform;

            // --- XR rig ---------------------------------------------------------
            var rig = InstantiateXRRig();
            rig.transform.position = Vector3.zero;
            SetupInteractionManager();

            // --- perspective ----------------------------------------------------
            var perspGO = new GameObject("PerspectiveController");
            var perspective = perspGO.AddComponent<PerspectiveController>();
            perspective.model = model;
            perspective.simulation = controller;
            perspective.xrRigRoot = FindRigRoot(rig.transform);
            perspective.rideSideOffset = 3.5f;

            // --- grabbable strain handle ---------------------------------------
            var anchor = new GameObject("StrainHandle_Anchor");
            anchor.transform.position = new Vector3(-0.6f, 1.25f, 0.8f);
            var handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handle.name = "StrainHandle (grab me to change amplitude)";
            handle.transform.position = anchor.transform.position;
            handle.transform.localScale = Vector3.one * 0.12f;
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
            MakeFloatingLabel("grab me:\namplitude h0", anchor.transform.position + new Vector3(0f, 0.12f, 0f),
                0.045f, new Color(0.6f, 0.95f, 0.95f), simGO.transform);

            // --- prediction task ------------------------------------------------
            var taskGO = new GameObject("PredictionTask");
            var task = taskGO.AddComponent<PredictionTask>();
            task.field = field;
            task.model = model;
            task.simulation = controller;

            // --- world-space control panel --------------------------------------
            var panel = BuildControlPanel(controller, model, parameters, perspective, task);
            task.ui = panel;

            // --- event system ----------------------------------------------------
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            var xrui = AddComponentByNames(es,
                "UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit");
            if (xrui == null) es.AddComponent<StandaloneInputModule>();

            // --- floating info label over the field ------------------------------
            MakeFloatingLabel(
                "simplified gravitational wave - strain h(t) = h0 sin(2pi f t + phi)\n" +
                "deformation EXAGGERATED, propagation SLOWED (real GWs move at c)",
                new Vector3(0f, 3.1f, 3f), 0.05f, new Color(1f, 0.9f, 0.4f), simGO.transform);

            // --- save -------------------------------------------------------------
            string scenePath = $"{Root}/Scenes/Main.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            Debug.Log($"[Project8] Scene saved: {scenePath}");
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

            if (hum != null)
            {
                var src = go.AddComponent<AudioSource>();
                src.clip = hum;
                src.loop = true;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.maxDistance = 10f;
                src.volume = 0f;
                src.playOnAwake = true;
                var cue = go.AddComponent<WavefrontAudio>();
                cue.model = model;
                cue.simulation = sim;
            }
            return go;
        }

        static void MakeFloatingLabel(string text, Vector3 pos, float charSize, Color color, Transform parent)
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
            // face the observer start position (origin, looking +z)
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            go.GetComponent<MeshRenderer>().sharedMaterial = _font != null ? _font.material : null;
        }

        // ------------------------------------------------------------------ UI panel
        static SimulationUI BuildControlPanel(SimulationController sim, GravitationalWaveModel model,
                                              SimulationParameters parameters,
                                              PerspectiveController perspective, PredictionTask task)
        {
            var canvasGO = new GameObject("ControlPanel");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)canvasGO.transform;
            rt.sizeDelta = new Vector2(950f, 620f);
            canvasGO.transform.position = new Vector3(1.55f, 1.45f, 1.05f);
            canvasGO.transform.localScale = Vector3.one * 0.001f;
            // face the user's starting position (eye ~1.6 m at the origin)
            Vector3 toUser = new Vector3(0f, 1.5f, 0f) - canvasGO.transform.position;
            canvasGO.transform.rotation = Quaternion.LookRotation(toUser.normalized, Vector3.up);

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
                               Vector2 pos, float width, UnityAction onClick)
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
            UnityEventTools.AddPersistentListener(btn.onClick, onClick);

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
