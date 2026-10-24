using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine.Events;
using Project8.Core;
using Project8.UI;
using Project8.Story;
using Project8.Evaluation;
using Project8.Interaction;
using Project8.AI;
using Project8.Visualization;

namespace Project8.EditorTools
{
    /// <summary>
    /// Cockpit UI builders for the redesign: one shared subtitle strip, a
    /// persistent Play/Pause dock, separate drawers (Controls / Mission &amp; Help /
    /// ET inspector lives inside Controls), the mission action dock and Rick's
    /// composer - all skinned with the lightweight procedural liquid-glass look
    /// (rounded smoke-glass panels, cool rim, top sheen; no GrabPass).
    /// </summary>
    public static partial class SceneBuilder
    {
        // ------------------------------------------------------ glass system --
        enum GlassRole { Panel, Button, Inset, Selected }

        static readonly Color GlassPanelTint = new Color(0.10f, 0.14f, 0.22f, 0.45f);
        static readonly Color GlassButtonTint = new Color(0.16f, 0.24f, 0.36f, 0.55f);
        static readonly Color GlassInsetTint = new Color(0.05f, 0.08f, 0.13f, 0.62f);
        static readonly Color GlassCardTint = new Color(0.12f, 0.18f, 0.28f, 0.50f);
        static readonly Color GlassShadowTint = new Color(0f, 0f, 0f, 0.38f);
        static readonly Color InkPrimary = new Color(0.95f, 0.97f, 1f);
        static readonly Color InkSecondary = new Color(0.72f, 0.76f, 0.85f);
        static readonly Color InkFaint = new Color(0.55f, 0.58f, 0.68f, 0.9f);

        static Material GlassMat(GlassRole role)
        {
            string path = $"{Root}/Materials/UI/M_UIGlass_{role}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Project8/UI/LiquidGlass");
            if (mat == null)
            {
                mat = new Material(shader) { name = $"M_UIGlass_{role}" };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (shader != null && mat.shader != shader) mat.shader = shader;
            switch (role)
            {
                case GlassRole.Panel:
                    mat.SetFloat("_CornerRadius", 18f); mat.SetFloat("_Sheen", 0.45f);
                    mat.SetFloat("_Rim", 0.55f); mat.SetFloat("_EdgeSoft", 1.6f);
                    mat.SetColor("_Color", Color.white); break;
                case GlassRole.Button:
                    mat.SetFloat("_CornerRadius", 14f); mat.SetFloat("_Sheen", 0.55f);
                    mat.SetFloat("_Rim", 0.7f); mat.SetFloat("_EdgeSoft", 1.4f);
                    mat.SetColor("_Color", Color.white); break;
                case GlassRole.Inset:
                    mat.SetFloat("_CornerRadius", 12f); mat.SetFloat("_Sheen", 0.08f);
                    mat.SetFloat("_Rim", 0.25f); mat.SetFloat("_EdgeSoft", 1.4f);
                    mat.SetColor("_Color", new Color(0.85f, 0.9f, 1f, 1f)); break;
                case GlassRole.Selected:
                    mat.SetFloat("_CornerRadius", 14f); mat.SetFloat("_Sheen", 0.6f);
                    mat.SetFloat("_Rim", 1.0f); mat.SetFloat("_EdgeSoft", 1.4f);
                    mat.SetColor("_Color", new Color(0.62f, 0.92f, 1f, 1f)); break;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Stretched rounded-glass background + a soft non-raycast
        /// shadow plate offset below it. Decorative - never blocks pointer.</summary>
        static LiquidGlassGraphic GlassBG(RectTransform parent, GlassRole role,
            Color tint, bool shadow = true)
        {
            if (shadow)
            {
                var sh = NewUIObject("GlassShadow", parent);
                Stretch(sh);
                sh.anchoredPosition = new Vector2(0f, -8f);
                var sg = sh.gameObject.AddComponent<LiquidGlassGraphic>();
                sg.material = GlassMat(GlassRole.Panel);
                sg.color = GlassShadowTint;
                sg.raycastTarget = false;
                sh.SetSiblingIndex(0); // rendered behind everything
            }
            var g = NewUIObject("Glass", parent);
            Stretch(g);
            g.SetSiblingIndex(shadow ? 1 : 0);
            var gr = g.gameObject.AddComponent<LiquidGlassGraphic>();
            gr.material = GlassMat(role);
            gr.color = tint;
            gr.raycastTarget = false;
            return gr;
        }

        /// <summary>A near-invisible ray-BLOCKING layer directly under a panel
        /// face: blank glass regions must still catch pointer and mouse-look
        /// rays instead of letting them fall through to the world (clicking
        /// empty glass must not rotate the cockpit). All decorative layers -
        /// shadow, glass face - stay raycast-free; only this blocker and the
        /// interactive controls take rays.</summary>
        static void PanelHitArea(RectTransform panel)
        {
            var go = new GameObject("PanelHitArea", typeof(RectTransform), typeof(Image));
            var r = (RectTransform)go.transform;
            r.SetParent(panel, false);
            Stretch(r);
            r.SetSiblingIndex(0);
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.001f);
            img.raycastTarget = true;
        }

        /// <summary>World-space canvas helper shared by every cockpit surface.
        /// TexCoord1 is enabled so the glass shader sees per-widget pixel size.</summary>
        static RectTransform MakeWorldCanvas(Transform ship, string name, Vector2 size,
            Vector3 localPos, float scale, Vector3 eye, bool interactive)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            if (interactive)
            {
                go.AddComponent<GraphicRaycaster>();
                AddComponentByNames(go,
                    "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            }
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            go.transform.SetParent(ship, true);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * scale;
            go.transform.rotation =
                Quaternion.LookRotation((go.transform.position - eye).normalized, Vector3.up);
            return rt;
        }

        static Button Btn(Transform parent, string name, string label, Vector2 pos,
                          float w, UnityAction onClick)
        {
            var t = MakeButton(parent, name, label, pos, w, onClick);
            return t.transform.parent.GetComponent<Button>();
        }

        // -------------------------------------------------------- subtitles --
        /// <summary>The single subtitle strip: canvas with NO raycaster, no
        /// background, just a white 28pt Text with a black shadow - story
        /// narration and Rick's answers share it, always visible.</summary>
        static RickSubtitles BuildSubtitleCanvas(Transform ship, Vector3 eye)
        {
            var rt = MakeWorldCanvas(ship, "SubtitleBar", new Vector2(1000f, 100f),
                new Vector3(0f, 0.72f, 1.40f), 0.0012f, eye, interactive: false);
            var subs = rt.gameObject.AddComponent<RickSubtitles>();
            var tr = NewUIObject("Text", rt);
            Stretch(tr);
            var text = tr.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var sh = tr.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
            sh.effectDistance = new Vector2(2f, -2f);
            subs.text = text;
            return subs;
        }

        // --------------------------------------------------- playback dock --
        /// <summary>Persistent Play/Pause dock lower-left - outside every drawer,
        /// always reachable. Delegates to StoryDirector.TogglePlayback.</summary>
        static void BuildPlaybackDock(Transform ship, Vector3 eye,
            SimulationController sim, StoryDirector story)
        {
            var rt = MakeWorldCanvas(ship, "GlobalPlayback", new Vector2(260f, 84f),
                new Vector3(-0.80f, 0.92f, 1.20f), 0.0013f, eye, interactive: true);
            PanelHitArea(rt);
            GlassBG(rt, GlassRole.Panel, GlassPanelTint);
            var dock = rt.gameObject.AddComponent<PlaybackDock>();
            dock.simulation = sim;
            dock.story = story;
            dock.label = MakeButton(rt, "Btn_PlayPause", "PLAY",
                new Vector2(0f, 14f), 220f, dock.OnPlayPause);
            dock.timeText = MakeText(rt, "Time", "t = 0.0 s", 15, FontStyle.Normal,
                TextAnchor.MiddleCenter, InkSecondary, new Vector2(0f, -28f),
                new Vector2(230f, 24f));
        }

        // -------------------------------------------------- mission actions --
        /// <summary>The mission's three contextual actions on their own small
        /// dock beside the rail - StoryDirector enables whichever applies.</summary>
        static void BuildMissionActionDock(Transform ship, Vector3 eye, StoryDirector story)
        {
            var rt = MakeWorldCanvas(ship, "MissionActionDock", new Vector2(260f, 72f),
                new Vector3(-0.80f, 1.12f, 1.20f), 0.0013f, eye, interactive: true);
            PanelHitArea(rt);
            GlassBG(rt, GlassRole.Panel, GlassPanelTint);
            // All three contextual actions share one rect - only the one the
            // mission phase needs is ever active, so nothing can overlap.
            story.startButton = Btn(rt, "Btn_Start", "START MISSION",
                Vector2.zero, 240f, story.StartMission);
            story.continueButton = Btn(rt, "Btn_Continue", "CONTINUE",
                Vector2.zero, 240f, story.ContinueMission);
            story.restartButton = Btn(rt, "Btn_Restart", "RESTART MISSION",
                Vector2.zero, 240f, story.RestartMission);
            story.continueButton.gameObject.SetActive(false);
            story.restartButton.gameObject.SetActive(false);
        }

        // --------------------------------------------------- mission & help --
        /// <summary>The MISSION and HELP drawer (cockpit.secondaryPanel): what
        /// this simulation is, how to fly it, and the honest list of every
        /// simplification - the disclosure that used to float in world space.</summary>
        static GameObject BuildMissionHelp(Transform ship, Vector3 eye,
            CockpitControls cockpit)
        {
            var rt = MakeWorldCanvas(ship, "MissionPanel", new Vector2(1120f, 1080f),
                new Vector3(0.12f, 1.60f, 1.45f), 0.001f, eye, interactive: true);
            PanelHitArea(rt);
            GlassBG(rt, GlassRole.Panel, GlassPanelTint);
            MakeText(rt, "Title", "MISSION & HELP", 28, FontStyle.Bold,
                TextAnchor.MiddleLeft, InkPrimary, new Vector2(-30f, 500f),
                new Vector2(700f, 40f));
            Btn(rt, "Btn_Close", "CLOSE  X", new Vector2(460f, 500f), 150f,
                cockpit.ShowMission);
            var body = MakeText(rt, "Body",
                "HOW TO FLY\n" +
                "- START / CONTINUE / RESTART live on the mission dock (left rail); " +
                "the lower-left dock always pauses and plays.\n" +
                "- CONTROLS drawer - WAVE LAB tab: shape the wave (amplitude, " +
                "frequency, exaggeration), save it into four slots, replay it at " +
                "the point you are observing.\n" +
                "- ET tab: select station D1/D2/D3, toggle its optical detail and " +
                "read the LF/HF channel readout.\n" +
                "- Desktop Enter advances the story; VR input is unchanged.\n\n" +
                "WHAT IS REAL / WHAT IS SIMPLIFIED\n" +
                "- Waves are emitted at merger and travel outward causally at a " +
                "chosen visualization speed - the causality is real, the speed " +
                "is not (real gravitational waves move at light speed).\n" +
                "- A real binary radiates through inspiral too; this demo only " +
                "shows the post-merger wave for teaching.\n" +
                "- Strain shown here is ~0.5; real detector strain is ~1e-21. " +
                "The laser markers are slowed enormously.\n" +
                "- Michelson principle is real: split -> different arm lengths -> " +
                "recombine at the splitter -> dark-port intensity.\n" +
                "- The ET triangle (3 stations, 60-degree arms, a nested LF/HF " +
                "pair per station) shows the 2020 concept baseline - not final " +
                "site geometry; drawn at 12 m where the real arms are ~10 km. " +
                "LF/HF share one qualitative strain - no separate noise curves.\n" +
                "- NOT simulated: Fabry-Perot build-up, recycling mirrors, " +
                "suspensions or any noise sources.\n" +
                "- The spacetime 'sheet' is a 2D embedding analogy - not a " +
                "material surface and not a solution of Einstein's equations.\n" +
                "- Riding the wave is a viewpoint, not a relativistic frame.",
                19, FontStyle.Normal, TextAnchor.UpperLeft, InkSecondary,
                new Vector2(0f, -60f), new Vector2(1020f, 940f));
            var loc = body.gameObject.AddComponent<LocalizedText>();
            loc.english = body.text;
            loc.chinese =
                "如何操作\n" +
                "- 开始/继续/重新开始任务在左侧任务坞上；左下角常驻坞随时播放/暂停。\n" +
                "- 「控制台」抽屉——波形实验页：调节波形（振幅、频率、夸张度），存入四个槽位，在当前观察点回放。\n" +
                "- ET 页：选择测站 D1/D2/D3，开关其光路细节，查看 LF/HF 通道读数。\n" +
                "- 桌面端回车键推进剧情；VR 输入不变。\n\n" +
                "哪些是真实的，哪些是简化的\n" +
                "- 波在并合时刻产生并因果地向外传播——因果性是真的，速度不是（真实引力波以光速传播）。\n" +
                "- 真实双星在旋近阶段同样辐射引力波；本演示为教学只展示并合后的波形。\n" +
                "- 此处应变约 0.5；真实探测器应变约 1e-21。光标记被大幅放慢。\n" +
                "- 迈克耳孙原理是真的：分束 → 不同臂长 → 在分束器重新汇合 → 暗端口强度。\n" +
                "- ET 三角形（3 个测站、60 度臂、每站一对嵌套 LF/HF）展示的是 2020 概念基线——并非最终台址几何；图中臂长 12 m，真实约 10 km。LF/HF 共用同一条定性应变——没有独立的噪声曲线。\n" +
                "- 未模拟：法布里-珀罗增益、循环镜、悬挂系统与任何噪声源。\n" +
                "- 时空“薄膜”是二维嵌入类比——不是实体表面，也不是爱因斯坦方程的解。\n" +
                "- “乘波而行”只是观察视角，不是相对论参考系。";
            loc.dutch =
                "HOE TE VLIEGEN\n" +
                "- START / DOORGAAN / HERSTARTEN staan op het missiedok (linker rail); " +
                "het linkerdok onder pauzeert en speelt altijd.\n" +
                "- CONTROLS-lade - tabblad WAVE LAB: vorm de golf (amplitude, " +
                "frequentie, overdrijving), sla hem op in vier slots en speel hem " +
                "af op het punt dat je bekijkt.\n" +
                "- ET-tab: kies station D1/D2/D3, schakel het optische detail om " +
                "en lees de LF/HF-kanalen.\n" +
                "- Enter op desktop schakelt het verhaal verder; VR-invoer is ongewijzigd.\n\n" +
                "WAT IS ECHT / WAT IS VEREENVOUDIGD\n" +
                "- Golven ontstaan bij de samensmelting en planten zich causaal " +
                "voort met een gekozen visualisatiesnelheid - de causaliteit is " +
                "echt, de snelheid niet (echte zwaartekrachtgolven gaan met " +
                "lichtsnelheid).\n" +
                "- Een echte dubbelster straalt ook tijdens de inspiraalfase; " +
                "deze demo toont alleen de golf na de samensmelting.\n" +
                "- Getoonde rek is ~0.5; echte detectorrek is ~1e-21. De " +
                "lichtmarkeringen zijn enorm vertraagd.\n" +
                "- Het Michelson-principe is echt: splitsen -> verschillende " +
                "armlengtes -> hercombineren bij de splitter -> donkere poort.\n" +
                "- De ET-driehoek (3 stations, 60-graden armen, een genest " +
                "LF/HF-paar per station) toont de concept-basislijn van 2020 - " +
                "geen definitieve locatiegeometrie; hier 12 m, de echte armen " +
                "zijn ~10 km. LF/HF delen een kwalitatieve rek - geen aparte " +
                "ruiscurves.\n" +
                "- NIET gesimuleerd: Fabry-Perot-opbouw, recycling-spiegels, " +
                "ophangingen of ruisbronnen.\n" +
                "- Het ruimtetijd-'vel' is een 2D-embeddinganalogie - geen " +
                "materieel oppervlak en geen oplossing van Einsteins vergelijkingen.\n" +
                "- Op de golf surfen is een gezichtspunt, geen relativistisch stelsel.";
            return rt.gameObject;
        }

        // ------------------------------------------------------------ rail --
        /// <summary>The collapsed menu rail on the left console wing: glass
        /// chip with the two drawer buttons.</summary>
        static GameObject BuildCompactPanel(Transform ship, CockpitControls cockpit,
            Vector3 eye)
        {
            var rt = MakeWorldCanvas(ship, "MenuRail", new Vector2(260f, 150f),
                new Vector3(-0.80f, 1.60f, 1.20f), 0.00115f, eye, interactive: true);
            PanelHitArea(rt);
            GlassBG(rt, GlassRole.Panel, GlassPanelTint);
            MakeText(rt, "RailTitle", "RICK · A2", 17, FontStyle.Bold,
                TextAnchor.MiddleCenter, Accent, new Vector2(0f, 52f),
                new Vector2(240f, 26f));
            MakeButton(rt, "Btn_Secondary", "MISSION & HELP",
                new Vector2(0f, 8f), 220f, cockpit.ShowMission);
            MakeButton(rt, "Btn_Controls", "CONTROLS",
                new Vector2(0f, -48f), 220f, cockpit.ShowControls);
            return rt.gameObject;
        }

        // ------------------------------------------------- Rick composer ----
        /// <summary>Rick's lower-right avatar + collapsible composer. The
        /// composer holds only a title, the question input, ASK and MIC at the
        /// right edge - answers go to the shared subtitles, never a bubble.</summary>
        static void BuildRickAvatar(Transform ship, Vector3 eye, AITutor tutor,
            Project8.UI.RickVoice voice, RickSubtitles subtitles)
        {
            var rt = MakeWorldCanvas(ship, "RickAvatar", new Vector2(480f, 260f),
                new Vector3(0.80f, 0.92f, 1.20f), 0.0011f, eye, interactive: true);
            // No root hit-area here: the composer opens/closes, so a full-size
            // blocker would swallow mouse-look rays across an invisible 480x260
            // region. Only the open composer window and the avatar chip (itself
            // a Button) intercept rays.
            var chat = rt.gameObject.AddComponent<TutorChatUI>();
            chat.tutor = tutor;
            chat.voice = voice;
            chat.subtitles = subtitles;

            var window = NewUIObject("ChatWindow", rt);
            window.anchoredPosition = new Vector2(0f, 45f);
            window.sizeDelta = new Vector2(460f, 160f);
            PanelHitArea(window);
            GlassBG(window, GlassRole.Panel, GlassPanelTint);
            MakeText(window, "Title", "RICK", 20, FontStyle.Bold, TextAnchor.MiddleLeft,
                Accent, new Vector2(-175f, 52f), new Vector2(120f, 28f));
            MakeText(window, "Hint", "AI tutor - answers appear as subtitles", 15,
                FontStyle.Italic, TextAnchor.MiddleLeft, InkFaint,
                new Vector2(20f, 52f), new Vector2(330f, 28f));
            chat.questionInput = MakeInput(window, "AskInput",
                new Vector2(-95f, -22f), 270f, "Ask Rick a question...");
            Btn(window, "Btn_Ask", "ASK", new Vector2(95f, -22f), 90f, chat.Ask);
            var micText = MakeButton(window, "Btn_Mic", "MIC",
                new Vector2(185f, -22f), 70f, chat.ToggleVoiceInput);
            chat.micButton = micText.transform.parent.GetComponent<Button>();
            chat.micLabel = micText;
            chat.chatWindow = window.gameObject;
            window.gameObject.SetActive(false);

            // Round avatar chip, always visible - same liquid-glass language
            // as the rest of the cockpit (rounded squircle + inset letter).
            var avatar = NewUIObject("Avatar", rt);
            avatar.anchoredPosition = new Vector2(0f, -80f);
            avatar.sizeDelta = new Vector2(80f, 80f);
            var avGlass = avatar.gameObject.AddComponent<LiquidGlassGraphic>();
            avGlass.material = GlassMat(GlassRole.Button);
            avGlass.color = new Color(0.20f, 0.40f, 0.58f, 0.62f);
            var avBtn = avatar.gameObject.AddComponent<Button>();
            avBtn.targetGraphic = avGlass;
            var avColors = avBtn.colors;
            avColors.highlightedColor = new Color(0.45f, 0.75f, 0.95f, 0.85f);
            avColors.pressedColor = new Color(0.60f, 0.90f, 1f, 0.95f);
            avBtn.colors = avColors;
            UnityEventTools.AddPersistentListener(avBtn.onClick, chat.ToggleChat);
            MakeText(avatar, "Face", "RICK", 15, FontStyle.Bold, TextAnchor.MiddleCenter,
                Color.white, Vector2.zero, avatar.sizeDelta);

            UnityEventTools.AddPersistentListener(tutor.onAnswer, chat.ShowAnswer);
        }

        // -------------------------------------------------- control panel ---
        /// <summary>The CONTROLS drawer (1120x1080) with two progressive-
        /// disclosure tabs. WAVE LAB: shape sliders, four slot cards, the
        /// inline save/load-or-confirm/cancel row, one transport row (Replay
        /// Current / Reset / .25x / .5x / 1x - Play/Pause stays on the
        /// persistent dock) and the mission tools. ET: station selector,
        /// detail toggle and the LF/HF channel readout.</summary>
        static SimulationUI BuildControlPanel(Transform ship, Vector3 eye,
            SimulationController sim, GravitationalWaveModel model,
            SimulationParameters parameters, PerspectiveController perspective,
            PredictionTask task, AITutor tutor, Project8.UI.RickVoice voice,
            EinsteinTelescopeDisplay et, StoryDirector story)
        {
            var libGO = new GameObject("WaveLibrary");
            var lib = libGO.AddComponent<WaveLibrary>();
            lib.parameters = parameters;
            lib.simulation = sim;
            lib.model = model;
            lib.story = story;

            var rt = MakeWorldCanvas(ship, "ControlPanel", new Vector2(1120f, 1080f),
                new Vector3(0.12f, 1.60f, 1.45f), 0.001f, eye, interactive: true);
            PanelHitArea(rt);
            GlassBG(rt, GlassRole.Panel, GlassPanelTint);

            var ui = rt.gameObject.AddComponent<SimulationUI>();
            ui.simulation = sim;
            ui.model = model;
            ui.parameters = parameters;
            ui.perspective = perspective;
            ui.story = story;

            var cockpit = ship.GetComponent<CockpitControls>();
            MakeText(rt, "Title", "CONTROL CONSOLE", 28, FontStyle.Bold,
                TextAnchor.MiddleLeft, InkPrimary, new Vector2(-30f, 500f),
                new Vector2(700f, 40f));
            Btn(rt, "Btn_Close", "CLOSE  X", new Vector2(460f, 500f), 150f,
                cockpit.TogglePanel);

            // ---- tabs --------------------------------------------------------
            var tabs = rt.gameObject.AddComponent<ConsoleTabs>();
            var wavePage = NewUIObject("WavePage", rt);
            Stretch(wavePage);
            var etPage = NewUIObject("ETPage", rt);
            Stretch(etPage);
            tabs.wavePage = wavePage.gameObject;
            tabs.etPage = etPage.gameObject;
            tabs.waveButton = Btn(rt, "Btn_TabWave", "WAVE LAB",
                new Vector2(-140f, 440f), 240f, null);
            tabs.etButton = Btn(rt, "Btn_TabET", "ET",
                new Vector2(140f, 440f), 240f, null);
            UnityEventTools.AddPersistentListener(tabs.waveButton.onClick, tabs.ShowWave);
            UnityEventTools.AddPersistentListener(tabs.etButton.onClick, tabs.ShowET);

            // =====================  WAVE PAGE  ================================
            MakeText(wavePage, "SecShape", "WAVE SHAPE", 20, FontStyle.Bold,
                TextAnchor.MiddleLeft, Accent, new Vector2(-30f, 395f),
                new Vector2(500f, 30f));
            ui.amplitudeSlider = MakeSlider(wavePage, "Slider_Amplitude",
                new Vector2(-140f, 350f), 600f, 0f, 1f, parameters.strainAmplitude,
                ui.SetAmplitude);
            ui.amplitudeValueText = MakeText(wavePage, "AmpValue", "", 20,
                FontStyle.Normal, TextAnchor.MiddleCenter, InkPrimary,
                new Vector2(330f, 350f), new Vector2(240f, 48f));
            MakeText(wavePage, "AmpLabel", "strain amplitude h0", 17,
                FontStyle.Normal, TextAnchor.MiddleCenter, InkFaint,
                new Vector2(-140f, 318f), new Vector2(600f, 28f));

            ui.frequencySlider = MakeSlider(wavePage, "Slider_Frequency",
                new Vector2(-140f, 260f), 600f, 0.05f, 4f, parameters.frequencyHz,
                ui.SetFrequency);
            ui.frequencyValueText = MakeText(wavePage, "FreqValue", "", 20,
                FontStyle.Normal, TextAnchor.MiddleCenter, InkPrimary,
                new Vector2(330f, 260f), new Vector2(240f, 48f));
            MakeText(wavePage, "FreqLabel", "frequency f (Hz)", 17,
                FontStyle.Normal, TextAnchor.MiddleCenter, InkFaint,
                new Vector2(-140f, 228f), new Vector2(600f, 28f));

            ui.exaggerationSlider = MakeSlider(wavePage, "Slider_Exaggeration",
                new Vector2(-140f, 170f), 600f, 0f, 2f,
                parameters.displacementExaggeration, ui.SetExaggeration);
            ui.exaggerationValueText = MakeText(wavePage, "ExagValue", "", 20,
                FontStyle.Normal, TextAnchor.MiddleCenter, InkPrimary,
                new Vector2(330f, 170f), new Vector2(240f, 48f));
            MakeText(wavePage, "ExagLabel",
                "exaggeration: 0 = real scale (invisible)  ->  2 = max", 17,
                FontStyle.Normal, TextAnchor.MiddleCenter, InkFaint,
                new Vector2(-140f, 138f), new Vector2(600f, 28f));

            // ---- saved waves: four independent slot cards (2x2) --------------
            MakeText(wavePage, "SecSlots", "SAVED WAVES", 20, FontStyle.Bold,
                TextAnchor.MiddleLeft, Accent, new Vector2(-30f, 90f),
                new Vector2(500f, 30f));
            lib.slotButtons = new Button[WaveLibrary.SlotCount];
            lib.slotTexts = new Text[WaveLibrary.SlotCount];
            for (int i = 0; i < WaveLibrary.SlotCount; i++)
            {
                Vector2 cardPos = new Vector2(
                    i % 2 == 0 ? -242f : 242f, i < 2 ? 20f : -84f);
                var card = NewUIObject("Card_Wave" + i, wavePage);
                card.anchoredPosition = cardPos;
                card.sizeDelta = new Vector2(460f, 88f);
                var cardGlass = card.gameObject.AddComponent<LiquidGlassGraphic>();
                cardGlass.material = GlassMat(GlassRole.Button);
                cardGlass.color = GlassCardTint;
                var cardBtn = card.gameObject.AddComponent<Button>();
                cardBtn.targetGraphic = cardGlass;
                var cc = cardBtn.colors;
                cc.highlightedColor = new Color(1.2f, 1.2f, 1.25f, 1f);
                cc.pressedColor = new Color(0.8f, 1.1f, 1.3f, 1f);
                cc.disabledColor = new Color(0.6f, 0.6f, 0.65f, 0.5f);
                cardBtn.colors = cc;
                int slot = i;
                UnityEventTools.AddIntPersistentListener(
                    cardBtn.onClick, lib.SelectSlot, slot);
                var cardText = MakeText(card, "Label", $"Wave {i + 1}\n(empty)", 19,
                    FontStyle.Normal, TextAnchor.MiddleCenter, InkPrimary,
                    Vector2.zero, card.sizeDelta);
                Stretch((RectTransform)cardText.transform);
                lib.slotButtons[i] = cardBtn;
                lib.slotTexts[i] = cardText;
            }

            // Save/Load and Confirm/Cancel share the SAME two positions - the
            // library activates exactly one pair depending on PendingOverwrite.
            var saveT = MakeButton(wavePage, "Btn_Save", "Save to 1",
                new Vector2(-140f, -176f), 240f, lib.SaveSelected);
            lib.saveButton = saveT.transform.parent.GetComponent<Button>();
            lib.saveButtonText = saveT;
            var loadT = MakeButton(wavePage, "Btn_Load", "Load 1",
                new Vector2(140f, -176f), 240f, lib.LoadSelected);
            lib.loadButton = loadT.transform.parent.GetComponent<Button>();
            lib.loadButtonText = loadT;
            lib.confirmButton = Btn(wavePage, "Btn_Confirm", "CONFIRM OVERWRITE",
                new Vector2(-140f, -176f), 240f, lib.ConfirmOverwrite);
            lib.cancelButton = Btn(wavePage, "Btn_Cancel", "CANCEL",
                new Vector2(140f, -176f), 240f, lib.CancelOverwrite);
            lib.confirmButton.gameObject.SetActive(false);
            lib.cancelButton.gameObject.SetActive(false);
            lib.hintText = MakeText(wavePage, "SlotHint", "", 16, FontStyle.Italic,
                TextAnchor.MiddleCenter, InkFaint, new Vector2(0f, -220f),
                new Vector2(900f, 34f));

            // ---- one transport row (exact non-overlapping spans) -------------
            ui.replayButton = Btn(wavePage, "Btn_Replay", "REPLAY CURRENT WAVE",
                new Vector2(-340f, -282f), 280f, ui.OnReplay);
            ui.resetButton = Btn(wavePage, "Btn_Reset", "RESET",
                new Vector2(-100f, -282f), 140f, ui.OnReset);
            Btn(wavePage, "Btn_SpeedQ", ".25x", new Vector2(60f, -282f), 100f,
                ui.OnSpeedQuarter);
            Btn(wavePage, "Btn_SpeedHalf", ".5x", new Vector2(190f, -282f), 100f,
                ui.OnSpeedHalf);
            Btn(wavePage, "Btn_Speed1", "1x", new Vector2(320f, -282f), 100f,
                ui.OnSpeedNormal);

            // ---- mission tools ------------------------------------------------
            ui.perspectiveButtonText = MakeButton(wavePage, "Btn_Ride",
                "Ride with the wave", new Vector2(-140f, -355f), 260f,
                ui.OnTogglePerspective);
            Btn(wavePage, "Btn_Predict", "Predict next mass",
                new Vector2(140f, -355f), 240f, task.BeginPrediction);

            ui.statusText = MakeText(wavePage, "Status", "", 18, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Color(0.65f, 0.9f, 0.7f),
                new Vector2(0f, -458f), new Vector2(1000f, 36f));
            MakeText(wavePage, "Legend",
                "orange = stretch   |   blue = squeeze   |   nodes glow as the wavefront passes",
                16, FontStyle.Normal, TextAnchor.MiddleCenter, InkFaint,
                new Vector2(0f, -500f), new Vector2(1000f, 30f));

            // =====================  ET PAGE  ==================================
            et.stationButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                et.stationButtons[i] = Btn(etPage, $"Btn_D{i + 1}", $"D{i + 1}",
                    new Vector2(-224f + i * 224f, 300f), 200f, null);
                UnityEventTools.AddIntPersistentListener(
                    et.stationButtons[i].onClick, et.SelectDetector, i);
            }
            Btn(etPage, "Btn_Detail", "DETAIL ON/OFF",
                new Vector2(0f, 220f), 240f, et.ToggleDetail);
            et.statusText = MakeText(etPage, "ETStatus", "", 18, FontStyle.Normal,
                TextAnchor.MiddleCenter, InkSecondary, new Vector2(0f, 168f),
                new Vector2(640f, 36f));

            var readout = NewUIObject("ETReadout", etPage);
            readout.anchoredPosition = new Vector2(0f, 100f);
            readout.sizeDelta = new Vector2(900f, 90f);
            GlassBG(readout, GlassRole.Inset, GlassInsetTint, shadow: false);
            et.inspectorText = MakeText(readout, "Text",
                "D1  LF  phase --   signal --\n      HF  phase --   signal --",
                22, FontStyle.Normal, TextAnchor.MiddleCenter, InkPrimary,
                Vector2.zero, new Vector2(880f, 80f));
            MakeText(etPage, "ETCaption",
                "3 stations - 60-degree arms - nested LF/HF pair per station - " +
                "12 m shown stands in for ~10 km real arms",
                16, FontStyle.Normal, TextAnchor.MiddleCenter, InkFaint,
                new Vector2(0f, 30f), new Vector2(1000f, 30f));

            // Initial state for edit-mode inspection and screenshots alike.
            wavePage.gameObject.SetActive(true);
            etPage.gameObject.SetActive(false);
            tabs.ShowWave();
            return ui;
        }

        // ------------------------------------------------- story director ---
        /// <summary>The StoryDirector component plus its three UI surfaces:
        /// subtitle presenter, mission action dock and the help drawer.</summary>
        static StoryDirector BuildStoryDirector(Transform ship, Vector3 eye,
            SimulationController sim, GravitationalWaveModel model,
            Transform earth, Transform binary, Transform observatory,
            Project8.UI.RickVoice voice, RickSubtitles subtitles)
        {
            var dirGO = new GameObject("StoryDirector");
            var director = dirGO.AddComponent<StoryDirector>();
            director.simulation = sim;
            director.model = model;
            director.ship = ship;
            director.earth = earth;
            director.binary = binary;
            director.ligo = observatory;
            director.voice = voice;
            director.subtitles = subtitles;
            // The presenter owns utterances once wired: narration voice is held
            // behind an in-flight AI answer instead of cutting it off.
            if (subtitles != null) subtitles.voice = voice;
            director.cockpit = ship.GetComponent<CockpitControls>();
            BuildMissionActionDock(ship, eye, director);
            return director;
        }
    }
}
