using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Project8.Core;
using Project8.UI;
using Project8.Visualization;

namespace Project8.Story
{
    /// <summary>
    /// The underground laboratory - the mission's second space. Built entirely at
    /// runtime by StoryDirector: a rock cavern ~60 m below the Earth prop, the
    /// Einstein Telescope triangle relocated onto its floor, a teaching Michelson
    /// interferometer on an optical bench (90-degree arms, labelled parts, a
    /// stream of light pulses), a guided step-by-step tour console, the loading
    /// veil used for the descent, and the persistent mission-log panel.
    ///
    /// The lab root stays DISABLED while the mission is in space - the cavern
    /// must never be visible from outside as a floating box - and wakes only
    /// during the descent, while the veil is black.
    ///
    /// Layout is authored relative to the ship's locked heading: the ship parks
    /// on a pad behind the demo bench, the bench sits at lab centre and the ET
    /// triangle model stands further forward - everything dead ahead.
    /// </summary>
    public class LabExperience : MonoBehaviour
    {
        StoryDirector _story;
        SimulationController _sim;
        GravitationalWaveModel _model;
        Transform _ship, _earth, _ligo;

        // --------------------------------------------------------- layout ---
        GameObject _labRoot;
        Mesh _interiorMesh;
        Vector3 _labCenter;   // room centre on the floor
        Vector3 _fwd, _right; // ship heading flattened at build time
        const float EarthCentreOffset = 2f;
        const float LabVisualScale = 0.2f;
        /// <summary>Uniform scale applied to the teaching interferometer root -
        /// the beams, mirrors, pulses and labels all sit in the demo's local
        /// space so one factor enlarges the whole optical layout ~2.4x while
        /// keeping every part inside the cavern bounds.</summary>
        const float DemoOpticScale = 2.4f;
        const float EarthRadiusGuess = 7.2f;

        public Vector3 ParkPosition { get; private set; }
        public Vector3 FocusPoint { get; private set; }   // demo interferometer
        public bool Ready { get; private set; }

        // ----------------------------------------------------- transition ---
        Transform _veilRoot;
        readonly List<Renderer> _veilWalls = new List<Renderer>();
        Material _veilMat;
        GameObject _loadingCard;
        Text _loadingText;

        Camera _cam;

        // ------------------------------------------------------------ tour ---
        class Step
        {
            public string titleEn, titleZh, titleNl;
            public string en, zh, nl;
            public int highlightA = -1;
            public int highlightB = -1;
            public bool highlightEt;
            public bool replayWave;
        }
        readonly List<Step> _steps = new List<Step>();
        int _step = -1;
        bool _stepReady;
        int _highlightA = -1, _highlightB = -1;
        bool _highlightEt;
        Coroutine _focusRoutine;

        // -------------------------------------------------------- materials --
        Material _rock, _floor, _metal, _console, _glass;
        Material _beamRed, _beamCyan, _glowCyan, _detectorGlow;

        // demo part focus points (world space), filled during build
        GameObject _demoRoot, _benchRoot;
        readonly List<GameObject> _demoLabels = new List<GameObject>();
        readonly List<Text> _partLabels = new List<Text>();
        /// <summary>Parallel to <see cref="_partLabels"/>: the world-space point
        /// the camera should look at for each part - the actual optical
        /// component, NOT the floating label above it.</summary>
        readonly List<Vector3> _partFocus = new List<Vector3>();
        Text _etLabel;
        int _laserLabel = -1, _splitterLabel = -1, _armLabel = -1;
        int _mirrorALabel = -1, _mirrorBLabel = -1, _detectorLabel = -1;
        Vector3 _laserPos, _splitterPos, _armMidPos, _mirrorAPos, _mirrorBPos,
                _detectorPos, _etPos;

        // ============================================================ build ==
        /// <summary>Called once by StoryDirector.Start, after the ship heading is
        /// locked: builds the cavern, relocates the ET display into it, spawns
        /// the demo bench and veil. Tour prompts use subtitles and the mission
        /// dock - no separate floating console.</summary>
        public void Build(StoryDirector story)
        {
            if (Ready) return;
            _story = story;
            _sim = story.simulation;
            _model = story.model;
            _ship = story.ship;
            _earth = story.earth;
            _ligo = story.ligo;
            if (_ship == null || _earth == null) return;

            MakeMaterials();

            _fwd = _ship.forward; _fwd.y = 0f;
            _fwd = _fwd.sqrMagnitude > 0.01f ? _fwd.normalized : Vector3.forward;
            _right = Quaternion.AngleAxis(90f, Vector3.up) * _fwd;

            _cam = Camera.main;
            if (_cam == null)
            {
                var cockpit = _ship.GetComponent<Interaction.CockpitControls>();
                if (cockpit != null) _cam = cockpit.seatedCamera;
            }

            _labCenter = _earth.position - Vector3.up * EarthCentreOffset;

            _labRoot = new GameObject("UndergroundLab");
            _labRoot.transform.position = _labCenter;
            _labRoot.transform.localScale = Vector3.one * LabVisualScale;
            ParkPosition = _labRoot.transform.TransformPoint(
                -_fwd * 7.5f + Vector3.up * 1.5f);
            _etPos = _labRoot.transform.TransformPoint(_fwd * 14f);

            BuildCavern();
            BuildEarthInterior();
            RelocateTelescope();
            BuildDemoBench();
            BuildVeil();
            BuildLoadingCard();
            DefineSteps();

            _labRoot.SetActive(false);

            // Interstellar-style dressing for both holes + the remnant.
            if (_story.binary != null)
            {
                var a = _story.binary.Find("BH_A");
                var b = _story.binary.Find("BH_B");
                var m = _story.binary.Find("MergedHole");
                AccretionDress.Apply(a);
                AccretionDress.Apply(b);
                AccretionDress.Apply(m);
            }

            // Glass-panel ghost fix at RUNTIME: GlassShadow is a Graphic (not
            // necessarily an Image), so querying Image missed the real offset
            // plate and left every panel looking doubled.
            foreach (var graphic in _ship.GetComponentsInChildren<Graphic>(true))
                if (graphic.name == "GlassShadow") graphic.enabled = false;

            RuntimeCockpitTheme.Apply(_ship);
            EnsureDockNextButton();

            // The persistent log rides with the ship - it exists in BOTH spaces.
            Transform avatar = _ship.Find("RickAvatar/Avatar");
            if (avatar == null) avatar = _ship.Find("RickAvatar");
            if (avatar != null)
            {
                var avatarSpot = avatar.GetComponent<UiSpotlightTarget>();
                if (avatarSpot == null) avatarSpot = avatar.gameObject.AddComponent<UiSpotlightTarget>();
                avatarSpot.id = "rick-avatar";
            }
            MissionLogUI.Attach(_ship, avatar, _cam);
            MissionLogUI.Add("Lab", Localize(
                "Mission log online - everything Rick explains and every answer lands here.",
                "任务记录已开启——Rick 的每段讲解和问答都会记在这里。",
                "Missielogboek actief - alle uitleg en antwoorden komen hier terecht."));

            Ready = true;
        }

        void MakeMaterials()
        {
            _rock = StdMat(new Color(0.10f, 0.09f, 0.085f), 0.12f);
            _floor = StdMat(new Color(0.14f, 0.15f, 0.18f), 0.45f);
            _metal = StdMat(new Color(0.45f, 0.47f, 0.52f), 0.5f);
            _console = StdMat(new Color(0.16f, 0.17f, 0.21f), 0.5f);
            _glass = StdMat(new Color(0.6f, 0.8f, 0.95f, 0.14f), 0.9f);
            SetupTransparent(_glass);

            _beamRed = AddMat(new Color(1f, 0.1f, 0.1f, 0.85f));
            _beamCyan = AddMat(new Color(0.25f, 0.9f, 1f, 0.85f));
            _glowCyan = StdMat(new Color(0.15f, 0.85f, 0.9f), 0.6f);
            _glowCyan.EnableKeyword("_EMISSION");
            _glowCyan.SetColor("_EmissionColor", new Color(0.1f, 1.2f, 1.3f));
            _detectorGlow = _glowCyan;
        }

        static Material StdMat(Color c, float gloss)
        {
            var m = new Material(Shader.Find("Standard")) { color = c };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            return m;
        }

        static Material AddMat(Color tint)
        {
            var m = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
            m.SetColor("_TintColor", tint);
            return m;
        }

        static void SetupTransparent(Material m)
        {
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static string Localize(string en, string zh, string nl) =>
            ExperienceSettings.IsChinese ? zh
                : ExperienceSettings.IsDutch && nl != null ? nl : en;

        // ---------------------------------------------------------- cavern --
        void BuildCavern()
        {
            var room = _labRoot.transform;
            const float W = 21f, H = 6.5f, T = 0.6f; // half-width, half-height, wall thickness

            MakeBox(room, "Floor", new Vector3(0f, -0.3f, 0f),
                new Vector3(W * 2f + 2f, T, W * 2f + 2f), _floor);
            MakeBox(room, "Ceiling", new Vector3(0f, H * 2f + 0.3f, 0f),
                new Vector3(W * 2f + 2f, T, W * 2f + 2f), _rock);
            MakeBox(room, "WallN", new Vector3(0f, H, W + T * 0.5f),
                new Vector3(W * 2f + 2f, H * 2f, T), _rock);
            MakeBox(room, "WallS", new Vector3(0f, H, -W - T * 0.5f),
                new Vector3(W * 2f + 2f, H * 2f, T), _rock);
            MakeBox(room, "WallE", new Vector3(W + T * 0.5f, H, 0f),
                new Vector3(T, H * 2f, W * 2f + 2f), _rock);
            MakeBox(room, "WallW", new Vector3(-W - T * 0.5f, H, 0f),
                new Vector3(T, H * 2f, W * 2f + 2f), _rock);

            // Rock texture suggestion: uneven darker lumps + strata seams.
            var rng = new System.Random(8);
            var lumpMat = StdMat(new Color(0.075f, 0.07f, 0.065f), 0.05f);
            for (int i = 0; i < 60; i++)
            {
                int wall = rng.Next(4);
                float a = (float)(rng.NextDouble() * 2 - 1) * (W - 1.5f);
                float y = (float)rng.NextDouble() * (H * 2f - 1f) + 0.5f;
                Vector3 p; Quaternion q = Quaternion.identity;
                float sx = 1f + (float)rng.NextDouble() * 2.2f;
                float sy = 0.5f + (float)rng.NextDouble() * 1.2f;
                float sz = 0.25f + (float)rng.NextDouble() * 0.35f;
                switch (wall)
                {
                    case 0: p = new Vector3(a, y, W - 0.1f); break;
                    case 1: p = new Vector3(a, y, -W + 0.1f); break;
                    case 2: p = new Vector3(W - 0.1f, y, a); q = Quaternion.Euler(0, 90, 0); break;
                    default: p = new Vector3(-W + 0.1f, y, a); q = Quaternion.Euler(0, 90, 0); break;
                }
                MakeBox(room, "RockLump", p, new Vector3(sx, sy, sz), lumpMat,
                    q * Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 10f - 5f));
            }

            // Ceiling light strips + warm point lights.
            for (int i = -2; i <= 2; i++)
            {
                MakeBox(room, "LightStrip", new Vector3(i * 8f, H * 2f - 0.15f, 0f),
                    new Vector3(0.35f, 0.08f, W * 1.4f), _glowCyan);
                var lgo = new GameObject("LabLight");
                lgo.transform.SetParent(room, false);
                lgo.transform.localPosition = new Vector3(i * 8f, H * 2f - 1f, 0f);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(0.85f, 0.92f, 1f);
                l.intensity = 1.1f;
                l.range = 26f;
            }

            // Landing pad where the ship materialises.
            Vector3 padLocal = _labRoot.transform.InverseTransformPoint(ParkPosition);
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "LandingPad";
            pad.transform.SetParent(room, false);
            pad.transform.localPosition = new Vector3(padLocal.x, 0.04f, padLocal.z);
            pad.transform.localScale = new Vector3(2.6f, 0.02f, 2.6f);
            pad.GetComponent<Renderer>().sharedMaterial = _glowCyan;
            KillCollider(pad);

            // A few facility props: crates and a conduit along one wall.
            for (int i = 0; i < 5; i++)
                MakeBox(room, "Crate", new Vector3(-W + 1.4f, 0.5f + 0.5f * (i % 2),
                        -8f + i * 2.6f), new Vector3(1.1f, 1f, 1.1f),
                    i % 2 == 0 ? _console : _metal);
            MakeBox(room, "Conduit", new Vector3(-W + 0.4f, 3.4f, 0f),
                new Vector3(0.25f, 0.25f, W * 1.7f), _metal);
        }

        void BuildEarthInterior()
        {
            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "EarthInteriorShell";
            shell.transform.SetParent(_labRoot.transform, false);
            shell.transform.localPosition =
                _labRoot.transform.InverseTransformPoint(_earth.position);
            shell.transform.localScale = Vector3.one *
                (2f * (EarthRadiusGuess - 0.15f) / LabVisualScale);

            // Flip the sphere faces so the rock surface renders from inside.
            var filter = shell.GetComponent<MeshFilter>();
            _interiorMesh = Object.Instantiate(filter.sharedMesh);
            _interiorMesh.name = "EarthInteriorShell_Inward";
            int[] triangles = _interiorMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }
            _interiorMesh.triangles = triangles;
            _interiorMesh.RecalculateNormals();
            _interiorMesh.RecalculateBounds();
            filter.sharedMesh = _interiorMesh;

            var renderer = shell.GetComponent<Renderer>();
            renderer.sharedMaterial = _rock;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            KillCollider(shell);
        }

        void RelocateTelescope()
        {
            if (_ligo == null) return;
            // The observatory no longer floats in space: it lives on the cavern
            // floor, forward of the demo bench, triangle still horizontal.
            _ligo.SetParent(_labRoot.transform, false);
            Vector3 local = _labRoot.transform.InverseTransformPoint(_etPos);
            _ligo.localPosition = new Vector3(local.x, 0.05f, local.z);
            _ligo.localRotation = Quaternion.identity;
            _ligo.localScale = Vector3.one;
            _etPos = _ligo.position;
            // The hidden in-space object is the same canonical ET, relocated
            // inside the cavern. Reveal it only on the final tour step so the
            // L-shaped principle demo is not visually doubled by the triangle.
            _ligo.gameObject.SetActive(false);
        }

        /// <summary>Keep the ship inside the scaled Earth interior even if the
        /// user flies through the cavern wall. The inward-facing shell remains
        /// visible around them instead of exposing the old space backdrop.</summary>
        public Vector3 ClampPilotPosition(Vector3 worldPosition)
        {
            if (_earth == null) return worldPosition;
            Vector3 offset = worldPosition - _earth.position;
            float safeRadius = EarthRadiusGuess - 2f;
            if (offset.sqrMagnitude > safeRadius * safeRadius)
                worldPosition = _earth.position + offset.normalized * safeRadius;
            return worldPosition;
        }

        // ------------------------------------------------------ demo bench --
        /// <summary>A table-height Einstein Telescope station: two arms meet at
        /// 60 degrees, with the laser, splitter, end mirrors, dark port and a
        /// slow visible pulse stream. The full three-station triangle appears
        /// as the final tour reveal.</summary>
        void BuildDemoBench()
        {
            var room = _labRoot.transform;
            const float arm = 3.3f;
            float benchY = 1.05f;

            // Optical bench under the optics - sized to the SCALED layout:
            // arm A reaches +7.9 local, arm B reaches (4.0, 6.9), the laser box
            // sits near -2.9 and the readout near (-1.1, -2.0), all well inside
            // the cavern half-width of ~21 local units.
            _benchRoot = MakeBox(room, "Bench", new Vector3(2.5f, benchY * 0.5f, 2.4f),
                new Vector3(11.5f, benchY, 9.8f), _metal);

            var demo = new GameObject("DemoInterferometer");
            _demoRoot = demo;
            demo.transform.SetParent(room, false);
            demo.transform.localPosition = new Vector3(0f, benchY + 0.06f, 0f);
            demo.transform.localRotation = Quaternion.identity;
            // Everything optical is authored in demo-local units, so this one
            // factor enlarges the whole instrument ~2.4x (user asked 2-3x).
            demo.transform.localScale = Vector3.one * DemoOpticScale;

            var li = demo.AddComponent<LaserInterferometer>();
            li.model = _model;
            li.simulation = _sim;
            li.armAxisA = Vector3.right;
            li.armAxisB = new Vector3(0.5f, 0f, 0.8660254f); // 60-degree ET arms
            li.armLength = arm;
            li.displayWavelength = 1.15f;
            li.markerSpeed = 1.5f;
            li.beamOffset = 0.05f;
            FocusPoint = demo.transform.position;

            Vector3 axA = li.armAxisA.normalized, axB = li.armAxisB.normalized;
            li.outboundA = MakeBeam(demo.transform, "Beam_OutA", axA * 0.02f, axA * arm, _beamRed, 0.06f);
            li.inboundA = MakeBeam(demo.transform, "Beam_InA", axA * arm, axA * 0.02f, _beamCyan, 0.06f);
            li.outboundB = MakeBeam(demo.transform, "Beam_OutB", axB * 0.02f, axB * arm, _beamRed, 0.06f);
            li.inboundB = MakeBeam(demo.transform, "Beam_InB", axB * arm, axB * 0.02f, _beamCyan, 0.06f);

            Vector3 splitNormal = (axA - axB).normalized; // Reflect(axA) == axB
            MakeBox(demo.transform, "BeamSplitter", Vector3.zero,
                new Vector3(0.05f, 0.42f, 0.42f), _glass,
                Quaternion.FromToRotation(Vector3.right, splitNormal));
            MakeBox(demo.transform, "NearMirror_A", axA * 0.35f,
                new Vector3(0.06f, 0.34f, 0.34f), _metal,
                Quaternion.FromToRotation(Vector3.right, axA));
            MakeBox(demo.transform, "NearMirror_B", axB * 0.35f,
                new Vector3(0.06f, 0.34f, 0.34f), _metal,
                Quaternion.FromToRotation(Vector3.right, axB));
            var mA = MakeBox(demo.transform, "EndMirror_A", axA * arm,
                new Vector3(0.09f, 0.45f, 0.45f), _metal,
                Quaternion.FromToRotation(Vector3.right, axA));
            var mB = MakeBox(demo.transform, "EndMirror_B", axB * arm,
                new Vector3(0.09f, 0.45f, 0.45f), _metal,
                Quaternion.FromToRotation(Vector3.right, axB));
            li.mirrorA = mA.transform;
            li.mirrorB = mB.transform;

            MakeBox(demo.transform, "LaserSource", -axA * 1.2f,
                new Vector3(0.65f, 0.28f, 0.28f), _console,
                Quaternion.FromToRotation(Vector3.right, axA));
            var pd = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pd.name = "Photodetector";
            pd.transform.SetParent(demo.transform, false);
            pd.transform.localPosition = -axB * 1.1f;
            pd.transform.localScale = Vector3.one * 0.3f;
            pd.GetComponent<Renderer>().sharedMaterial = _detectorGlow;
            KillCollider(pd);
            li.photodetector = pd.GetComponent<Renderer>();

            li.inputBeam = MakeBeam(demo.transform, "Beam_Input", -axA * 1.1f, Vector3.zero, _beamRed, 0.05f);
            li.outputBeam = MakeBeam(demo.transform, "Beam_Output", Vector3.zero, -axB * 1.05f, _beamCyan, 0.05f);
            li.signalTrace = MakeBeam(demo.transform, "SignalTrace",
                new Vector3(-1.6f, -0.7f, -1.3f), new Vector3(1.6f, -0.7f, -1.3f), _beamCyan, 0.03f);

            li.markerA = MakePulse(demo.transform, "Marker_A");
            li.markerB = MakePulse(demo.transform, "Marker_B");
            li.pulseTrainA = new[] { MakePulse(demo.transform, "Pulse_A1"), MakePulse(demo.transform, "Pulse_A2") };
            li.pulseTrainB = new[] { MakePulse(demo.transform, "Pulse_B1"), MakePulse(demo.transform, "Pulse_B2") };

            // A light over the enlarged bench.
            var lgo = new GameObject("BenchLight");
            lgo.transform.SetParent(room, false);
            lgo.transform.localPosition = new Vector3(2.5f, 4.4f, 2.4f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.95f, 0.85f);
            l.intensity = 1.6f;
            l.range = 18f;

            // Transform local optics coordinates into world coordinates before
            // placing labels. The cavern itself is scaled, so adding unscaled
            // world offsets made the old labels float far away from their parts.
            _laserPos = demo.transform.TransformPoint(-axA * 1.2f);
            _splitterPos = demo.transform.position;
            _armMidPos = demo.transform.TransformPoint((axA + axB).normalized * 1.7f);
            _mirrorAPos = demo.transform.TransformPoint(axA * arm);
            _mirrorBPos = demo.transform.TransformPoint(axB * arm);
            _detectorPos = demo.transform.TransformPoint(-axB * 1.1f);

            // Labels hover a fixed WORLD height above their (now larger) parts;
            // the focus list keeps the raw component position for the camera.
            _laserLabel = AddPartLabel(_laserPos + Vector3.up * 0.26f, _laserPos,
                Localize("LASER", "激光器", "LASER"));
            _splitterLabel = AddPartLabel(_splitterPos + Vector3.up * 0.30f, _splitterPos,
                Localize("BEAM SPLITTER", "分光镜", "STRAALDELER"));
            _armLabel = AddPartLabel(_armMidPos + Vector3.up * 0.24f, _armMidPos,
                Localize("ET ARMS · 60°", "ET 干涉臂 · 60°", "ET-ARMEN · 60°"));
            _mirrorALabel = AddPartLabel(_mirrorAPos + Vector3.up * 0.30f, _mirrorAPos,
                Localize("MIRROR", "反射镜", "SPIEGEL"));
            _mirrorBLabel = AddPartLabel(_mirrorBPos + Vector3.up * 0.30f, _mirrorBPos,
                Localize("MIRROR", "反射镜", "SPIEGEL"));
            _detectorLabel = AddPartLabel(_detectorPos + Vector3.up * 0.26f, _detectorPos,
                Localize("DETECTOR", "探测器", "DETECTOR"));

            var etLabel = MakeLabel(_etPos + Vector3.up * 0.9f,
                Localize("EINSTEIN TELESCOPE · ET",
                         "爱因斯坦望远镜 · ET", "EINSTEIN TELESCOPE · ET"));
            _etLabel = etLabel.GetComponentInChildren<Text>();
            etLabel.SetActive(false);
        }

        int AddPartLabel(Vector3 worldPos, Vector3 focusPos, string text)
        {
            var label = MakeLabel(worldPos, text);
            _demoLabels.Add(label);
            var textComponent = label.GetComponentInChildren<Text>();
            _partLabels.Add(textComponent);
            _partFocus.Add(focusPos);
            return _partLabels.Count - 1;
        }

        Transform MakePulse(Transform parent, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.18f;
            go.GetComponent<Renderer>().sharedMaterial = _glowCyan;
            KillCollider(go);
            return go.transform;
        }

        /// <summary>Small floating world-space label - a canvas with one Text,
        /// kept readable by the shared Billboard component.</summary>
        GameObject MakeLabel(Vector3 worldPos, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(_labRoot.transform, true);
            go.transform.position = worldPos;
            go.transform.localScale = Vector3.one * 0.0045f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(220f, 44f);
            go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var t = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            t.transform.SetParent(go.transform, false);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = Vector2.zero;
            t.rectTransform.offsetMax = Vector2.zero;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 30;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(0.7f, 0.92f, 1f, 0.95f);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var shadow = t.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
            shadow.effectDistance = new Vector2(1f, -1f);
            if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(t);
            t.text = text;
            var billboard = go.AddComponent<CanvasBillboard>();
            billboard.targetCamera = _cam;
            return go;
        }

        static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        Text AddText(Transform parent, string name, string s, int size,
                     FontStyle style, TextAnchor anchor, Color color)
        {
            var rt = NewRect(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = s;
            return t;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // -------------------------------------------------------- the veil --
        /// <summary>Six black walls boxed around the ship: during the descent they
        /// fade in as the hull enters the rock, and the teleport happens while
        /// the view is fully covered. Cockpit panels (closer in) stay visible.</summary>
        void BuildVeil()
        {
            _veilMat = StdMat(new Color(0f, 0f, 0f, 0f), 0f);
            SetupTransparent(_veilMat);

            _veilRoot = new GameObject("DescentVeil").transform;
            _veilRoot.SetParent(_ship, false);
            _veilRoot.localPosition = new Vector3(0f, 1.1f, 0f);
            const float R = 2.6f;
            // Thin slabs - a cube face renders from both sides, so the walls are
            // black whether the user looks at them from inside or outside.
            AddVeilWall(Vector3.forward * R, new Vector3(R * 2f, R * 2f, 0.05f));
            AddVeilWall(Vector3.back * R, new Vector3(R * 2f, R * 2f, 0.05f));
            AddVeilWall(Vector3.left * R, new Vector3(0.05f, R * 2f, R * 2f));
            AddVeilWall(Vector3.right * R, new Vector3(0.05f, R * 2f, R * 2f));
            AddVeilWall(Vector3.up * R, new Vector3(R * 2f, 0.05f, R * 2f));
            AddVeilWall(Vector3.down * R, new Vector3(R * 2f, 0.05f, R * 2f));
            SetVeil(0f);
        }

        void AddVeilWall(Vector3 localPos, Vector3 scale)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Cube);
            q.name = "VeilWall";
            q.transform.SetParent(_veilRoot, false);
            q.transform.localPosition = localPos;
            q.transform.localScale = scale;
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = _veilMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            KillCollider(q);
            _veilWalls.Add(r);
        }

        void SetVeil(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            _veilMat.color = new Color(0f, 0f, 0f, alpha);
        }

        void BuildLoadingCard()
        {
            var go = new GameObject("LoadingCard", typeof(RectTransform));
            go.transform.SetParent(_ship, false);
            go.transform.localPosition = new Vector3(0f, 1.0f, 1.4f);
            go.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
            go.transform.localScale = Vector3.one * 0.0012f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(520f, 120f);
            go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            _loadingText = AddText(rt, "Text", "", 26, FontStyle.Bold,
                TextAnchor.MiddleCenter, new Color(0.75f, 0.92f, 1f));
            Stretch(_loadingText.rectTransform);
            if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(_loadingText);
            _loadingCard = go;
            _loadingCard.SetActive(false);
        }

        /// <summary>Clone the dock's CONTINUE button into a NEXT button that
        /// advances the lab tour - so step controls live on the same mission
        /// dock as START/CONTINUE, never on a floating panel in the lab.</summary>
        void EnsureDockNextButton()
        {
            GameObject go;
            if (_story.nextButton != null)
            {
                go = _story.nextButton.gameObject;
                go.SetActive(false);
            }
            else
            {
                if (_story.continueButton == null) return;
                var src = _story.continueButton.gameObject;
                go = Object.Instantiate(src, src.transform.parent);
                go.name = "Btn_Next";
                go.SetActive(false);
                var btn = go.GetComponent<Button>();
                btn.onClick = new Button.ButtonClickedEvent();
                btn.onClick.AddListener(_story.Advance);
                _story.nextButton = btn;
            }

            var spot = go.GetComponent<UiSpotlightTarget>();
            if (spot == null) spot = go.AddComponent<UiSpotlightTarget>();
            spot.id = "lab-next";
            var label = go.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = Localize("NEXT  ▸", "下一步  ▸", "VOLGENDE  ▸");
                var localized = label.GetComponent<LocalizedText>();
                if (localized != null)
                {
                    localized.english = "NEXT";
                    localized.chinese = "下一步";
                    localized.dutch = "VOLGENDE";
                }
                if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(label);
            }
        }

        // -------------------------------------------------------- the tour ---
        void DefineSteps()
        {
            _steps.Add(new Step
            {
                titleEn = "Welcome to the underground lab",
                titleZh = "欢迎来到地下实验室",
                titleNl = "Welkom in het ondergrondse lab",
                en = "Rick: Welcome to the underground lab. The detector is built deep inside " +
                     "the Earth because up on the surface wind, traffic and tiny tremors " +
                     "would shake the mirrors and ruin the measurement. Down here it is " +
                     "quiet. Let me show you how it works - step by step. Press NEXT.",
                zh = "Rick：欢迎来到地下实验室。探测器建在地球深处，因为地面上风、车流和细微" +
                     "震动都会晃动镜片、毁掉测量，地下才够安静。我一步一步讲给你听，点「下一步」。",
                nl = "Rick: Welkom in het ondergrondse lab. De detector ligt diep in de " +
                     "aarde omdat wind, verkeer en kleine trillingen boven de spiegels " +
                     "zouden laten beven en de meting verpesten. Hier is het stil. " +
                     "Ik leg het stap voor stap uit - druk op VOLGENDE.",
            });
            _steps.Add(new Step
            {
                titleEn = "1. The laser", titleZh = "1. 激光器", titleNl = "1. De laser",
                en = "Rick: This box is the laser. It produces light of one pure colour - " +
                     "every wave marching in step, like soldiers. Watch the single beam " +
                     "leave the box; it is about to be cut in two.",
                zh = "Rick：这个盒子是激光器，它发出单一颜色的光——所有光波像士兵齐步走一样" +
                     "整齐同步。看这束光从盒子里出来，它马上要被一分为二。",
                nl = "Rick: Dit kastje is de laser. Hij maakt licht van één zuivere kleur - " +
                     "alle golven marcheren in de pas als soldaten. Zie de ene straal " +
                     "het kastje verlaten; die wordt zo in tweeën gesplitst.",
                highlightA = 0
            });
            _steps.Add(new Step
            {
                titleEn = "2. The beam splitter", titleZh = "2. 分光镜", titleNl = "2. De straaldeler",
                en = "Rick: The tilted glass plate in the middle is the beam splitter. " +
                     "Half the light passes straight through; the other half is reflected " +
                     "into a second arm. In the Einstein Telescope station the arms open " +
                     "at 60 degrees, not LIGO's 90 degrees.",
                zh = "Rick：中间斜放的玻璃片是分光镜：一半光直穿过去，另一半被反射进第二条臂。" +
                     "爱因斯坦望远镜测站的两臂夹角是 60 度，不是 LIGO 的 90 度。",
                nl = "Rick: De schuine glasplaat in het midden is de straaldeler. De " +
                     "helft gaat rechtdoor; de andere helft wordt de tweede arm in " +
                     "gekaatst. Bij de Einstein Telescope staan de armen 60 graden uit " +
                     "elkaar, niet 90 graden zoals bij LIGO.",
                highlightA = 1
            });
            _steps.Add(new Step
            {
                titleEn = "3. Down the arms", titleZh = "3. 沿臂前进", titleNl = "3. De armen op",
                en = "Rick: Each beam races to the far end of its arm. The glowing dots " +
                     "are light pulses, slowed down enormously so you can follow them. " +
                     "In the real detector the arms are kilometres long and the light " +
                     "bounces back and forth hundreds of times.",
                zh = "Rick：两束光各自冲向臂的尽头。发光的小点是光脉冲，被大大放慢好让你看清。" +
                     "真实探测器的臂有好几公里长，光要在里面来回跑数百趟。",
                nl = "Rick: Elke straal racet naar het einde van zijn arm. De lichte " +
                     "stipjes zijn lichtpulsen, enorm vertraagd zodat je ze kunt volgen. " +
                     "In de echte detector zijn de armen kilometers lang en stuitert " +
                     "het licht honderden keren heen en weer.",
                highlightA = 2
            });
            _steps.Add(new Step
            {
                titleEn = "4. The mirrors", titleZh = "4. 反射镜", titleNl = "4. De spiegels",
                en = "Rick: At the end of each arm a mirror sends the light straight " +
                     "back. These mirrors are the 'test masses': they hang almost " +
                     "perfectly free, so when space itself stretches or squeezes, " +
                     "they move with it.",
                zh = "Rick：每条臂的尽头有一面反射镜，把光原路送回。这些镜子就是「测试质量」：" +
                     "它们几乎完全自由地悬着，空间本身拉伸或挤压时，镜子会跟着动。",
                nl = "Rick: Aan het einde van elke arm stuurt een spiegel het licht " +
                     "terug. Die spiegels zijn de 'testmassa's': ze hangen vrijwel " +
                     "volledig vrij, dus als de ruimte zelf uitrekt of inkrimpt, " +
                     "bewegen ze mee.",
                highlightA = 3, highlightB = 4
            });
            _steps.Add(new Step
            {
                titleEn = "5. Recombining - the dark port", titleZh = "5. 重新合束——暗端口",
                titleNl = "5. Hercombineren - de donkere poort",
                en = "Rick: Both beams come home to the splitter and overlap again. " +
                     "Here is the clever part: when the two arms are exactly the same " +
                     "length, the two waves arrive perfectly out of step - every peak " +
                     "meets a dip and they cancel. The detector stays dark.",
                zh = "Rick：两束光回到分光镜重新叠加。奥妙来了：当两条臂长度完全相等时，两列" +
                     "光波错开半个波长——波峰正好对上波谷，互相抵消，探测器一片黑暗。",
                nl = "Rick: Beide stralen komen terug bij de deler en overlappen weer. " +
                     "Nu het slimme deel: als de armen exact even lang zijn, komen de " +
                     "golven precies in tegenfase aan - elke piek ontmoet een dal en " +
                     "ze doven elkaar uit. De detector blijft donker.",
                highlightA = 5
            });
            _steps.Add(new Step
            {
                titleEn = "6. The wave arrives", titleZh = "6. 引力波抵达",
                titleNl = "6. De golf arriveert", replayWave = true,
                en = "Rick: Now watch - the same gravitational wave you saw cross the " +
                     "surface reaches us underground. It stretches one arm while it " +
                     "squeezes the other, so the two light trips fall out of step: " +
                     "light leaks through and the detector flickers. That flicker IS " +
                     "the measurement - from it we read how much space moved.",
                zh = "Rick：仔细看——你在地表看到的那道引力波现在传到地下。它把一条臂拉长、" +
                     "同时把另一条臂压短，两路光程不再一致：一部分光漏出来，探测器开始闪烁。" +
                     "这一闪一烁就是测量本身——我们从中读出空间动了多少。",
                nl = "Rick: Kijk - dezelfde zwaartekrachtgolf die je boven zag bereikt " +
                     "ons hieronder. Hij rekt de ene arm uit en drukt de andere in, " +
                     "dus de lichtreizen lopen uit de pas: er lekt licht door en de " +
                     "detector flikkert. Dat flikkeren ÍS de meting - eruit lezen we " +
                     "hoe sterk de ruimte bewoog.",
                highlightA = 2, highlightB = 5
            });
            _steps.Add(new Step
            {
                titleEn = "7. The Einstein Telescope", titleZh = "7. 爱因斯坦望远镜",
                titleNl = "7. De Einstein Telescope",
                en = "Rick: This bench is ONE Einstein Telescope interferometer channel: " +
                     "its arms meet at 60 degrees. The full detector ahead has THREE " +
                     "stations arranged as an equilateral triangle; each corner has a " +
                     "low- and high-frequency instrument, six interferometers in total. " +
                     "Its real arms are 10 km long. This is a teaching twin - light is " +
                     "slowed, motion exaggerated, and the optical cavities and suspension " +
                     "noise are simplified. Fly closer, or press RESTART MISSION to repeat.",
                zh = "Rick：光学台上展示的是爱因斯坦望远镜的一条干涉仪通道，两臂夹角为 60 度。" +
                     "前方完整的 ET 有三个测站，排成等边三角形；每个角各有一台低频和一台高频仪器，" +
                     "总共六台干涉仪，真实臂长约 10 公里。这里是教学数字孪生：光被放慢、运动被夸大，" +
                     "谐振腔和悬挂噪声做了简化。可以飞近观察，或点「重新开始任务」重看。",
                nl = "Rick: Deze opstelling is ÉÉN interferometerkanaal van de Einstein " +
                     "Telescope: de armen staan 60 graden uit elkaar. De volledige ET " +
                     "heeft DRIE stations in een gelijkzijdige driehoek; elke hoek bevat " +
                     "een laag- en hoogfrequent instrument, zes interferometers in totaal. " +
                     "De echte armen zijn 10 km lang. Dit is een leermodel: licht is vertraagd, " +
                     "beweging overdreven en de optische caviteiten vereenvoudigd. Vlieg dichterbij " +
                     "of druk op MISSIE HERSTARTEN om opnieuw te beginnen.",
                highlightEt = true
            });
        }

        /// <summary>Called by StoryDirector once the ship is parked on the lab
        /// pad: begin the guided tour at step 0.</summary>
        public void StartTour()
        {
            if (_steps.Count == 0) return;
            ShowStep(0);
        }

        public void NextStep()
        {
            if (!_stepReady) return;
            if (_step >= _steps.Count - 1) { _story.OnTourFinished(); return; }
            ShowStep(_step + 1);
        }

        public void PrevStep()
        {
            if (_step > 0) ShowStep(_step - 1);
        }

        public void ResetTour()
        {
            _step = -1;
            _stepReady = false;
            _highlightA = -1;
            _highlightB = -1;
            _highlightEt = false;
            if (_etLabel != null) _etLabel.gameObject.SetActive(false);
            ApplyLabelHighlights();
            // Mission restarts back in space: hide the cavern again so it can
            // never be seen as a floating box below the Earth.
            if (_labRoot != null) _labRoot.SetActive(false);
            if (_ligo != null) _ligo.gameObject.SetActive(false);
            SetDemoStationVisible(true);
            if (_story.nextButton != null) _story.nextButton.gameObject.SetActive(false);
        }

        void ShowStep(int i)
        {
            _step = Mathf.Clamp(i, 0, _steps.Count - 1);
            var s = _steps[_step];
            UiSpotlightTarget.Clear();
            _stepReady = false;
            if (_story.nextButton != null)
                _story.nextButton.interactable = false;
            bool showTelescope = _step == _steps.Count - 1;
            if (_ligo != null) _ligo.gameObject.SetActive(showTelescope);
            SetDemoStationVisible(!showTelescope);
            _highlightA = s.highlightA;
            _highlightB = s.highlightB;
            _highlightEt = s.highlightEt;
            if (_etLabel != null) _etLabel.gameObject.SetActive(showTelescope);
            ApplyLabelHighlights();
            GuideCameraToStep(s);
            _story.Narrate(s.en, s.zh, s.nl);
            if (s.replayWave) _story.ReplayWaveAtFocus(FocusPoint);
            RefreshNextButton();
            _story.PromptAfterNarration("lab-next", 3f, () =>
            {
                _stepReady = true;
                if (_story.nextButton != null)
                    _story.nextButton.interactable = true;
            });
        }

        void SetDemoStationVisible(bool visible)
        {
            if (_demoRoot != null) _demoRoot.SetActive(visible);
            if (_benchRoot != null) _benchRoot.SetActive(visible);
            foreach (var label in _demoLabels)
                if (label != null) label.SetActive(visible);
        }

        /// <summary>The world-space point the camera should aim at for this
        /// step: the real optical component stored in <see cref="_partFocus"/>,
        /// or the ET triangle's centre for the reveal. Two highlighted parts
        /// are aimed at their midpoint.</summary>
        Vector3 FocusForStep(Step step)
        {
            if (step.highlightEt)
                return _etPos + Vector3.up * 0.8f;

            bool hasA = step.highlightA >= 0 && step.highlightA < _partFocus.Count;
            Vector3 a = hasA ? _partFocus[step.highlightA] : FocusPoint;
            bool hasB = step.highlightB >= 0 && step.highlightB < _partFocus.Count;
            if (!hasB) return a;
            return (a + _partFocus[step.highlightB]) * 0.5f;
        }

        /// <summary>Re-aims the camera at the step's component and gently glides
        /// the ship to a viewing standoff so the part is in clear view. Any
        /// user input (mouse, sticks, keys) cancels the guidance instantly.</summary>
        void GuideCameraToStep(Step step)
        {
            if (_ship == null) return;
            var cockpit = _story.cockpit != null
                ? _story.cockpit : _ship.GetComponent<Interaction.CockpitControls>();
            if (cockpit == null) return;
            Vector3 focus = FocusForStep(step);
            // Standoff behind the user's current bearing, a bit above the part.
            Vector3 away = _ship.position - focus;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -_ship.forward;
            away.y = 0f;
            away.Normalize();
            float standoff = step.highlightEt ? 3.2f : 1.9f;
            Vector3 vantage = focus + away * standoff + Vector3.up * 0.55f;
            vantage = _story.ClampLabPosition(vantage);
            cockpit.GuideLookAt(focus, vantage);
        }

        void ApplyLabelHighlights()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            for (int i = 0; i < _partLabels.Count; i++)
            {
                bool hot = i == _highlightA || i == _highlightB;
                ApplyLabelStyle(_partLabels[i], hot, pulse);
            }
            ApplyLabelStyle(_etLabel, _highlightEt, pulse);
        }

        static void ApplyLabelStyle(Text label, bool highlighted, float pulse)
        {
            if (label == null) return;
            label.color = highlighted
                ? Color.Lerp(new Color(0.58f, 0.88f, 1f, 0.95f),
                             new Color(1f, 0.88f, 0.42f, 1f), pulse)
                : new Color(0.7f, 0.92f, 1f, 0.95f);
            label.fontSize = highlighted ? Mathf.RoundToInt(32f + pulse * 4f) : 30;
            var shadow = label.GetComponent<Shadow>();
            if (shadow != null)
            {
                shadow.effectColor = highlighted
                    ? new Color(0.2f, 0.85f, 1f, 0.35f + pulse * 0.55f)
                    : new Color(0f, 0f, 0f, 0.72f);
                shadow.effectDistance = highlighted
                    ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
            }
        }

        void RefreshNextButton()
        {
            if (_story.nextButton != null)
            {
                var label = _story.nextButton.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    bool final = _step >= _steps.Count - 1;
                    string text = final
                        ? Localize("FINISH", "完成", "KLAAR")
                        : Localize($"NEXT  {_step + 1}/{_steps.Count}",
                                   $"下一步  {_step + 1}/{_steps.Count}",
                                   $"VOLGENDE  {_step + 1}/{_steps.Count}");
                    label.text = text;
                    var localized = label.GetComponent<LocalizedText>();
                    if (localized != null)
                    {
                        localized.english = text;
                        localized.chinese = text;
                        localized.dutch = text;
                    }
                    if (ExperienceSettings.IsChinese)
                        LocalizedText.UseChineseFont(label);
                }
            }
        }

        void Update()
        {
            ApplyLabelHighlights();
        }

        void OnDestroy()
        {
            if (_interiorMesh != null) Destroy(_interiorMesh);
        }

        // ------------------------------------------------------ the descent --
        /// <summary>Begin the camera dive: ship glides to a point above Earth,
        /// plunges into the crust behind the fading veil, then teleports into
        /// the cavern while the view is black - the "loading" beat - and the
        /// veil lifts on the lab. Calls back StoryDirector.OnLabArrived().</summary>
        public void BeginDescent()
        {
            if (!Ready) { _story.OnLabArrived(); return; }
            StartCoroutine(DescendRoutine());
        }

        IEnumerator DescendRoutine()
        {
            // Leg 1: climb/drift to a point just above the surface impact site.
            Vector3 entry = _earth.position + Vector3.up * (EarthRadiusGuess + 3.5f);
            yield return GlideAndFace(_ship, entry, _earth.position, 4.2f);

            // Leg 2: dive into the crust - the veil closes as we enter the rock.
            float contactY = _earth.position.y + EarthRadiusGuess;
            Vector3 inside = _earth.position;
            while ((_ship.position - inside).sqrMagnitude > 1f)
            {
                _ship.position = Vector3.MoveTowards(_ship.position, inside,
                    4.5f * Time.deltaTime);
                Vector3 towardEarth = _earth.position - _ship.position;
                if (towardEarth.sqrMagnitude > 0.01f)
                {
                    Quaternion lookAtEarth =
                        Quaternion.LookRotation(towardEarth.normalized, Vector3.up);
                    _ship.rotation = Quaternion.Slerp(_ship.rotation, lookAtEarth,
                        Time.deltaTime * 1.8f);
                }
                SetVeil(Mathf.Clamp01((contactY + 1.4f - _ship.position.y) / 1.8f));
                yield return null;
            }
            SetVeil(1f);

            // Loading beat: wake the lab while the view is fully covered.
            _labRoot.SetActive(true);
            if (_loadingCard != null)
            {
                _loadingCard.SetActive(true);
                if (_loadingText != null)
                    _loadingText.text = Localize(
                        "ENTERING THE UNDERGROUND LAB ...",
                        "正在进入地下实验室 ...",
                        "ONDERGRONDS LABORATORIUM BETREDEN ...");
            }
            _ship.SetPositionAndRotation(ParkPosition,
                Quaternion.LookRotation(_fwd, Vector3.up));
            yield return new WaitForSeconds(1.4f);
            if (_loadingCard != null) _loadingCard.SetActive(false);

            // Reveal.
            float a = 1f;
            while (a > 0f)
            {
                a -= Time.deltaTime / 1.2f;
                SetVeil(a);
                yield return null;
            }
            SetVeil(0f);
            _story.OnLabArrived();
        }

        IEnumerator GlideAndFace(Transform t, Vector3 target, Vector3 lookAt, float speed)
        {
            while ((t.position - target).sqrMagnitude > 0.6f)
            {
                t.position = Vector3.MoveTowards(t.position, target, speed * Time.deltaTime);
                Vector3 direction = lookAt - t.position;
                if (direction.sqrMagnitude > 0.01f)
                {
                    Quaternion facing = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    t.rotation = Quaternion.Slerp(t.rotation, facing, Time.deltaTime * 1.8f);
                }
                yield return null;
            }
        }

        // ------------------------------------------------------------ misc ---
        GameObject MakeBox(Transform parent, string name, Vector3 localPos,
                           Vector3 size, Material mat, Quaternion? localRot = null)
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

        LineRenderer MakeBeam(Transform parent, string name, Vector3 a, Vector3 b,
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

        static void KillCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
        }
    }
}
