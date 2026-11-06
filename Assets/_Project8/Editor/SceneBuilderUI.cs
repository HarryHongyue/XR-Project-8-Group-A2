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
using Project8.Perspective;

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

        /// <summary>One rounded smoke-glass face per panel. Avoid the old offset
        /// shadow plate: it looked like a second misaligned panel in VR.</summary>
        static LiquidGlassGraphic GlassBG(RectTransform parent, GlassRole role,
            Color tint)
        {
            var g = NewUIObject("Glass", parent);
            Stretch(g);
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
            // ConstantPixelSize scaler: on a world-space canvas it only raises
            // canvas.scaleFactor, i.e. the pixel density text glyphs rasterize
            // at - without it every label rasterizes undersampled and reads as
            // a blurred double-strike in the cockpit. Layout is untouched.
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 2.5f;
            scaler.referencePixelsPerUnit = 100f;
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
            SimulationController sim, StoryDirector story,
            RickSubtitles subtitles = null)
        {
            var rt = MakeWorldCanvas(ship, "GlobalPlayback", new Vector2(260f, 64f),
                new Vector3(-0.94f, 0.92f, 1.20f), 0.0013f, eye, interactive: true);
            // No backing plate: the button's own glass face is the whole dock -
            // an extra panel behind it only read as dead weight.
            var dock = rt.gameObject.AddComponent<PlaybackDock>();
            dock.simulation = sim;
            dock.story = story;
            dock.subtitles = subtitles;
            dock.label = MakeButton(rt, "Btn_PlayPause", "PLAY",
                new Vector2(0f, 0f), 220f, dock.OnPlayPause);
            rt.gameObject.AddComponent<UiSpotlightTarget>().id = "play";
        }

        // -------------------------------------------------- mission actions --
        /// <summary>The mission's three contextual actions on their own small
        /// dock beside the rail - StoryDirector enables whichever applies.</summary>
        static void BuildMissionActionDock(Transform ship, Vector3 eye, StoryDirector story)
        {
            var rt = MakeWorldCanvas(ship, "MissionActionDock", new Vector2(260f, 72f),
                new Vector3(-0.94f, 1.12f, 1.20f), 0.0013f, eye, interactive: true);
            // Bare contextual buttons - no backing plate behind them.
            // All three contextual actions share one rect - only the one the
            // mission phase needs is ever active, so nothing can overlap.
            // 220 px matches the rail and playback dock exactly - the earlier
            // 240 px action row stuck out past every sibling button.
            story.startButton = Btn(rt, "Btn_Start", "START MISSION",
                Vector2.zero, 220f, story.StartMission);
            story.continueButton = Btn(rt, "Btn_Continue", "CONTINUE",
                Vector2.zero, 220f, story.ContinueMission);
            story.restartButton = Btn(rt, "Btn_Restart", "RESTART MISSION",
                Vector2.zero, 220f, story.RestartMission);
            story.continueButton.gameObject.SetActive(false);
            story.restartButton.gameObject.SetActive(false);
            // Lab-tour NEXT lives on the same dock - the guided tour no longer
            // needs its own floating mini panel to advance.
            story.nextButton = Btn(rt, "Btn_Next", "NEXT",
                Vector2.zero, 220f, story.Advance);
            story.nextButton.gameObject.SetActive(false);
            story.nextButton.gameObject.AddComponent<UiSpotlightTarget>().id = "lab-next";
            story.startButton.gameObject.AddComponent<UiSpotlightTarget>().id = "start";
            story.continueButton.gameObject.AddComponent<UiSpotlightTarget>().id = "continue";
            story.restartButton.gameObject.AddComponent<UiSpotlightTarget>().id = "restart";
        }

        // --------------------------------------------------- mission & help --
        /// <summary>The MISSION and HELP drawer (cockpit.secondaryPanel): what
        /// this simulation is, how to fly it, and the honest list of every
        /// simplification - the disclosure that used to float in world space.</summary>
        static GameObject BuildMissionHelp(Transform ship, Vector3 eye,
            CockpitControls cockpit)
        {
            var rt = MakeWorldCanvas(ship, "MissionPanel", new Vector2(1120f, 1080f),
                new Vector3(0.12f, 1.35f, 1.45f), 0.001f, eye, interactive: true);
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
                "frequency, exaggeration), save it into four slots; PREDICT WAVE " +
                "previews your settings at the point you are observing.\n" +
                "- ET tab: select station D1/D2/D3, toggle its optical detail, " +
                "read the LF/HF channel readout, and run PREDICT NEXT STATION - " +
                "the wave freezes and you answer with D1/D2/D3.\n" +
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
                "- 「控制台」抽屉——波形实验页：调节波形（振幅、频率、夸张度），存入四个槽位，「预测波形」在当前观察点播放你的参数效果。\n" +
                "- ET 页：选择测站 D1/D2/D3，开关其光路细节，查看 LF/HF 通道读数；" +
                "「预测下一测站」会把波冻结在半路，用 D1/D2/D3 作答。\n" +
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
                "frequentie, overdrijving), sla hem op in vier slots; VOORSPEL " +
                "GOLF toont je instellingen op het punt dat je bekijkt.\n" +
                "- ET-tab: kies station D1/D2/D3, schakel het optische detail " +
                "om, lees de LF/HF-kanalen, en gebruik VOORSPEL VOLGEND " +
                "STATION - de golf bevriest en je antwoordt met D1/D2/D3.\n" +
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
            var rt = MakeWorldCanvas(ship, "MenuRail", new Vector2(260f, 110f),
                new Vector3(-0.94f, 1.60f, 1.20f), 0.00115f, eye, interactive: true);
            // Bare rail: just the two drawer buttons on their own glass -
            // the decorative "RICK · A2" title chip meant nothing to users.
            MakeButton(rt, "Btn_Secondary", "MISSION & HELP",
                new Vector2(0f, 22f), 220f, cockpit.ShowMission);
            var controlsText = MakeButton(rt, "Btn_Controls", "CONTROLS",
                new Vector2(0f, -34f), 220f, cockpit.ShowControls);
            controlsText.transform.parent.gameObject
                .AddComponent<UiSpotlightTarget>().id = "controls";
            return rt.gameObject;
        }

        // ------------------------------------------------- Rick composer ----
        /// <summary>Rick's lower-right avatar + collapsible composer. The
        /// composer holds only a title, the question input, ASK and MIC at the
        /// right edge - answers go to the shared subtitles, never a bubble.</summary>
        static void BuildRickAvatar(Transform ship, Vector3 eye, AITutor tutor,
            Project8.UI.RickVoice voice, RickSubtitles subtitles)
        {
            // The avatar + everything anchored to it (composer below, mission
            // history above) must sit CLEAR of the control panel on the right
            // (panel spans ship-local x in [-0.44, 0.68]): park the canvas far
            // right so no edge of any child can slide under the panel.
            var rt = MakeWorldCanvas(ship, "RickAvatar", new Vector2(480f, 260f),
                new Vector3(1.09f, 1.02f, 1.20f), 0.0011f, eye, interactive: true);
            // No root hit-area here: the composer opens/closes, so a full-size
            // blocker would swallow mouse-look rays across an invisible 480x260
            // region. Only the open composer window and the avatar chip (itself
            // a Button) intercept rays.
            var chat = rt.gameObject.AddComponent<TutorChatUI>();
            chat.tutor = tutor;
            chat.voice = voice;
            chat.subtitles = subtitles;
            // Wake-word + microphone + Whisper path: "Hey Rick" (Windows) or the
            // MIC button (everywhere) -> record -> transcribe -> ask -> spoken
            // answer. Endpoint stays empty until WHISPER_ENDPOINT is provided.
            var vi = tutor.gameObject.AddComponent<Project8.AI.VoiceInteraction>();
            vi.tutor = tutor;
            vi.chat = chat;
            vi.subtitles = subtitles;
            vi.voice = voice;
            chat.voiceInput = vi;

            var window = NewUIObject("ChatWindow", rt);
            // Keep the question composer below-left of the (now bigger) avatar;
            // the persistent history board is stacked above it and must never
            // overlap. Right edge ends 10 px left of the chip's left edge.
            window.anchoredPosition = new Vector2(-300f, -190f);
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

            // Round avatar chip, always visible, with its own runtime-drawn
            // face sprite so the scene never depends on an unsaved editor texture.
            var avatar = NewUIObject("Avatar", rt);
            avatar.anchoredPosition = new Vector2(0f, -70f);
            avatar.sizeDelta = new Vector2(120f, 120f);
            var avGlass = avatar.gameObject.AddComponent<LiquidGlassGraphic>();
            avGlass.material = GlassMat(GlassRole.Button);
            avGlass.color = new Color(0.20f, 0.40f, 0.58f, 0.62f);
            var avBtn = avatar.gameObject.AddComponent<Button>();
            avBtn.targetGraphic = avGlass;
            var avColors = avBtn.colors;
            avColors.normalColor = Color.white;
            avColors.highlightedColor = new Color(0.82f, 0.94f, 1f, 1f);
            avColors.pressedColor = new Color(0.58f, 0.82f, 0.96f, 1f);
            avBtn.colors = avColors;
            UnityEventTools.AddPersistentListener(avBtn.onClick, chat.ToggleChat);
            // Procedural cartoon face so the button reads as a character, not a
            // label: pale skin, spiky cyan hair, unibrow, baggy eyes, drool.
            var face = NewUIObject("Face", avatar);
            face.anchorMin = Vector2.zero; face.anchorMax = Vector2.one;
            face.offsetMin = new Vector2(6f, 16f); face.offsetMax = new Vector2(-6f, -4f);
            var faceImg = face.gameObject.AddComponent<Image>();
            faceImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/RickFace.png");
            faceImg.preserveAspect = true;
            faceImg.raycastTarget = false;
            face.gameObject.AddComponent<RickFaceIcon>();
            var avText = MakeText(avatar, "Tag", "RICK", 11, FontStyle.Bold,
                TextAnchor.MiddleCenter, Color.white, new Vector2(0f, -34f),
                new Vector2(80f, 20f));
            var avHover = avatar.gameObject.AddComponent<ButtonLabelHover>();
            avHover.label = avText;
            avatar.gameObject.AddComponent<UiSpotlightTarget>().id = "rick-avatar";

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
                new Vector3(0.12f, 1.35f, 1.45f), 0.001f, eye, interactive: true);
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
            tabs.waveButton.gameObject.AddComponent<UiSpotlightTarget>().id = "tab-wave";
            tabs.etButton.gameObject.AddComponent<UiSpotlightTarget>().id = "tab-et";

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

            // ---- saved waves: four compact slot cards in ONE row -------------
            MakeText(wavePage, "SecSlots", "SAVED WAVES", 20, FontStyle.Bold,
                TextAnchor.MiddleLeft, Accent, new Vector2(-30f, 90f),
                new Vector2(500f, 30f));
            lib.slotButtons = new Button[WaveLibrary.SlotCount];
            lib.slotTexts = new Text[WaveLibrary.SlotCount];
            for (int i = 0; i < WaveLibrary.SlotCount; i++)
            {
                Vector2 cardPos = new Vector2(-357f + i * 238f, 14f);
                var card = NewUIObject("Card_Wave" + i, wavePage);
                card.anchoredPosition = cardPos;
                card.sizeDelta = new Vector2(205f, 60f);
                var cardGlass = card.gameObject.AddComponent<LiquidGlassGraphic>();
                cardGlass.material = GlassMat(GlassRole.Button);
                cardGlass.color = GlassCardTint;
                var cardBtn = card.gameObject.AddComponent<Button>();
                cardBtn.targetGraphic = cardGlass;
                var cc = cardBtn.colors;
                cc.normalColor = Color.white;
                cc.highlightedColor = new Color(0.86f, 0.95f, 1f, 1f);
                cc.pressedColor = new Color(0.68f, 0.88f, 1f, 1f);
                cc.disabledColor = new Color(0.6f, 0.6f, 0.65f, 0.5f);
                cardBtn.colors = cc;
                int slot = i;
                UnityEventTools.AddIntPersistentListener(
                    cardBtn.onClick, lib.SelectSlot, slot);
                var cardText = MakeText(card, "Label", $"Wave {i + 1}\n(empty)", 14,
                    FontStyle.Normal, TextAnchor.MiddleCenter, InkPrimary,
                    Vector2.zero, card.sizeDelta);
                Stretch((RectTransform)cardText.transform);
                var cardHover = card.gameObject.AddComponent<ButtonLabelHover>();
                cardHover.label = cardText;
                lib.slotButtons[i] = cardBtn;
                lib.slotTexts[i] = cardText;
            }

            // Save/Load and Confirm/Cancel share the SAME two positions - the
            // library activates exactly one pair depending on PendingOverwrite.
            var saveT = MakeButton(wavePage, "Btn_Save", "Save to 1",
                new Vector2(-140f, -62f), 240f, lib.SaveSelected);
            lib.saveButton = saveT.transform.parent.GetComponent<Button>();
            lib.saveButtonText = saveT;
            var loadT = MakeButton(wavePage, "Btn_Load", "Load 1",
                new Vector2(140f, -62f), 240f, lib.LoadSelected);
            lib.loadButton = loadT.transform.parent.GetComponent<Button>();
            lib.loadButtonText = loadT;
            lib.confirmButton = Btn(wavePage, "Btn_Confirm", "CONFIRM OVERWRITE",
                new Vector2(-140f, -62f), 240f, lib.ConfirmOverwrite);
            lib.cancelButton = Btn(wavePage, "Btn_Cancel", "CANCEL",
                new Vector2(140f, -62f), 240f, lib.CancelOverwrite);
            lib.confirmButton.gameObject.SetActive(false);
            lib.cancelButton.gameObject.SetActive(false);
            lib.hintText = MakeText(wavePage, "SlotHint", "", 16, FontStyle.Italic,
                TextAnchor.MiddleCenter, InkFaint, new Vector2(0f, -108f),
                new Vector2(900f, 34f));

            // ---- action row under the slots: RESET (left) | PREDICT WAVE -----
            // "Predict wave" = play the wave with the CURRENT slider values -
            // the learner predicts what their parameter choices will look like,
            // then watches the preview. Distinct from the station prediction.
            ui.resetButton = Btn(wavePage, "Btn_Reset", "RESET",
                new Vector2(-170f, -160f), 300f, ui.OnReset);
            ui.replayButton = Btn(wavePage, "Btn_Replay", "PREDICT WAVE",
                new Vector2(170f, -160f), 300f, ui.OnReplay);

            // ---- speed row below it: playback rates only (the station
            // prediction moved to the ET page where its answer keys live) ----
            Btn(wavePage, "Btn_SpeedQ", ".25x", new Vector2(-280f, -220f), 160f,
                ui.OnSpeedQuarter);
            Btn(wavePage, "Btn_SpeedHalf", ".5x", new Vector2(0f, -220f), 160f,
                ui.OnSpeedHalf);
            Btn(wavePage, "Btn_Speed1", "1x", new Vector2(280f, -220f), 160f,
                ui.OnSpeedNormal);

            ui.statusText = MakeText(wavePage, "Status", "", 18, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Color(0.65f, 0.9f, 0.7f),
                new Vector2(0f, -300f), new Vector2(1000f, 36f));
            MakeText(wavePage, "Legend",
                "orange = stretch   |   blue = squeeze   |   the ring spreads outward from the merger",
                16, FontStyle.Normal, TextAnchor.MiddleCenter, InkFaint,
                new Vector2(0f, -342f), new Vector2(1000f, 30f));

            // =====================  ET PAGE  ==================================
            // The page used to sit half-empty - station pickers, readout and
            // the (relocated) prediction task now fill it at a larger scale.
            et.stationButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                et.stationButtons[i] = Btn(etPage, $"Btn_D{i + 1}", $"D{i + 1}",
                    new Vector2(-320f + i * 320f, 335f), 280f, null);
                var dRt = (RectTransform)et.stationButtons[i].transform;
                dRt.sizeDelta = new Vector2(280f, 68f);
                var dLabel = et.stationButtons[i].GetComponentInChildren<Text>();
                if (dLabel != null) dLabel.fontSize = 26;
                UnityEventTools.AddIntPersistentListener(
                    et.stationButtons[i].onClick, et.SelectDetector, i);
                // During a prediction round the same buttons are the answer keys.
                UnityEventTools.AddIntPersistentListener(
                    et.stationButtons[i].onClick, task.AnswerStation, i);
                et.stationButtons[i].gameObject.AddComponent<UiSpotlightTarget>()
                    .id = $"station-{i + 1}";
            }
            var detailBtn = Btn(etPage, "Btn_Detail", "DETAIL ON/OFF",
                new Vector2(0f, 252f), 320f, et.ToggleDetail);
            ((RectTransform)detailBtn.transform).sizeDelta = new Vector2(320f, 56f);
            et.statusText = MakeText(etPage, "ETStatus", "", 20, FontStyle.Normal,
                TextAnchor.MiddleCenter, InkSecondary, new Vector2(0f, 205f),
                new Vector2(760f, 40f));

            var readout = NewUIObject("ETReadout", etPage);
            readout.anchoredPosition = new Vector2(0f, 122f);
            readout.sizeDelta = new Vector2(980f, 110f);
            GlassBG(readout, GlassRole.Inset, GlassInsetTint);
            et.inspectorText = MakeText(readout, "Text",
                "D1  LF  phase --   signal --\n      HF  phase --   signal --",
                26, FontStyle.Normal, TextAnchor.MiddleCenter, InkPrimary,
                Vector2.zero, new Vector2(960f, 100f));
            MakeText(etPage, "ETCaption",
                "3 stations - 60-degree arms - nested LF/HF pair per station - " +
                "12 m shown stands in for ~10 km real arms",
                18, FontStyle.Normal, TextAnchor.MiddleCenter, InkFaint,
                new Vector2(0f, 56f), new Vector2(1000f, 32f));

            // ---- prediction task: lives HERE, next to its D1/D2/D3 keys ----
            MakeText(etPage, "SecPredict", "PREDICTION TASK", 20, FontStyle.Bold,
                TextAnchor.MiddleCenter, Accent, new Vector2(0f, -8f),
                new Vector2(500f, 32f));
            var predictBtn = MakeButton(etPage, "Btn_Predict", "PREDICT NEXT STATION",
                new Vector2(0f, -85f), 460f, task.BeginPrediction);
            ((RectTransform)predictBtn.transform.parent).sizeDelta =
                new Vector2(460f, 68f);
            predictBtn.fontSize = 22;
            predictBtn.transform.parent.gameObject
                .AddComponent<UiSpotlightTarget>().id = "predict";
            MakeText(etPage, "PredictHint",
                "freezes the wave mid-flight - answer with D1 / D2 / D3 above", 17,
                FontStyle.Italic, TextAnchor.MiddleCenter, InkFaint,
                new Vector2(0f, -140f), new Vector2(900f, 32f));
            MakeText(etPage, "PredictHint2",
                "physics hint: the station nearest the merger registers the front first",
                17, FontStyle.Italic, TextAnchor.MiddleCenter, InkFaint,
                new Vector2(0f, -185f), new Vector2(900f, 32f));

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
