using UnityEngine;
using UnityEngine.UI;
using Project8.Core;
using Project8.Interaction;
using Project8.UI;
using Project8.Visualization;

namespace Project8.Story
{
    /// <summary>
    /// Drives the storyboard sequence of the Main scene. The whole mission keeps ONE
    /// fixed heading: the ship always faces the binary-Earth line at 90 degrees, and
    /// only ever translates along the wave axis (pure lateral motion).
    ///
    ///   1. Waiting        - parked side-on, narration, START MISSION
    ///   2. WatchingMerger - simulation released; ship stays parked while the holes
    ///                       inspiral and merge on screen
    ///   3. Riding         - once the burst is on its way the ship follows the
    ///                       wavefront laterally, slightly behind it, heading locked
    ///   4. SlidingEarth   - short lateral slide into the Earth observation spot
    ///   5. EarthPause     - parked beside Earth; CONTINUE button; the narration
    ///                       invites the user to play with the wave controls first
    ///   6. Diving         - the ship dives toward Earth and sinks into the crust
    ///                       while the descent veil fades the view to black
    ///   7. EnteringLab    - loading beat: the ship materialises inside the
    ///                       underground laboratory and the veil lifts
    ///   8. InLab          - the guided step-by-step tour of the interferometer
    ///                       (LabExperience); free flight stays available
    ///   9. Done           - tour finished; RESTART MISSION
    /// </summary>
    public class StoryDirector : MonoBehaviour
    {
        public SimulationController simulation;
        public GravitationalWaveModel model;
        public Transform ship;
        public Transform earth;
        public Transform binary;
        [Tooltip("The Einstein Telescope observatory root (relocated into the lab).")]
        public Transform ligo;
        public CockpitControls cockpit;
        public RickVoice voice;

        [Header("UI wiring")]
        [Tooltip("Shared subtitle presenter - story narration and AI answers.")]
        public RickSubtitles subtitles;
        [Tooltip("Legacy narration Text - only used when no subtitle service exists.")]
        public Text narrationText;
        public Button startButton;
        public Button continueButton;
        public Button restartButton;
        [Tooltip("Lab tour NEXT button on the mission dock - visible only while " +
                 "the guided underground tour is running.")]
        public Button nextButton;

        [Header("Choreography")]
        [Tooltip("How far past the ship the wave must travel before we start moving. " +
                 "~7 m at the slowed visualization speed reads as a 1-2 second beat " +
                 "after the merger flash, so the burst is visibly born first.")]
        public float departLeadMeters = 7f;
        [Tooltip("Distance the ship trails behind the wavefront while riding. Larger " +
                 "values keep the strongest crest ahead in view instead of on top of us.")]
        public float rideLagBehindWavefront = 5f;
        public float rideBlendSpeed = 2f;
        public float slideBlendSpeed = 1.2f;
        public Vector3 earthWatchOffset = new Vector3(0f, 3f, -18f);
        [Tooltip("Legacy: oblique overview offset of the old in-space observatory " +
                 "spot - the lab is now underground; kept for scene compatibility.")]
        public Vector3 ligoWatchOffset = new Vector3(0f, 11f, -14f);

        /// <summary>The underground laboratory + tour, built at runtime in Start.</summary>
        public LabExperience lab;

        enum Phase { Waiting, WatchingMerger, Riding, SlidingEarth, EarthPause,
                     Diving, EnteringLab, InLab, Done }
        Phase _phase = Phase.Waiting;
        public bool CanPilot => _phase == Phase.Waiting || _phase == Phase.EarthPause ||
                                _phase == Phase.InLab || _phase == Phase.Done;
        public bool InLab => _phase == Phase.InLab || _phase == Phase.Done;
        public Vector3 ClampLabPosition(Vector3 position) =>
            lab != null ? lab.ClampPilotPosition(position) : position;
        /// <summary>Replay/Reset/Load are only offered at the stable observation
        /// phases - mid-transit they are disabled and the methods no-op, so a
        /// wave preview can never freeze the journey without an escape.</summary>
        public bool CanPreview => CanPilot;
        /// <summary>Parked at the start, mission not yet begun (dock START state).</summary>
        public bool MissionWaiting => _phase == Phase.Waiting;

        /// <summary>Wave-preview mode: the simulation clock runs/stands but all
        /// automatic mission choreography (ride + slides) is suspended.</summary>
        public bool InPreview => _preview;
        bool _preview;
        bool _canStartMission = true;
        Coroutine _promptRoutine;
        Vector3 _initialShipPos;
        Quaternion _initialShipRot;
        bool _poseCaptured;

        float _earthHitS;
        Vector3 _rideOffset;

        /// <summary>Store the parked ship pose RESTART MISSION returns to. Called
        /// once by Start; exposed so editor regression can stage the same state.</summary>
        public void CaptureInitialPose()
        {
            _initialShipPos = ship.position;
            _initialShipRot = ship.rotation;
            _poseCaptured = true;
        }

        void Start()
        {
            simulation.Pause();

            // The heading is set ONCE and never changed again: face the binary-Earth
            // line at exactly 90 degrees. All later motion is lateral translation only.
            Vector3 dir = model.PropagationDirection;
            Vector3 toLine = binary.position - ship.position;
            toLine -= dir * Vector3.Dot(toLine, dir);
            toLine.y = 0f;
            if (toLine.sqrMagnitude > 0.01f)
                ship.rotation = Quaternion.LookRotation(toLine.normalized, Vector3.up);
            CaptureInitialPose();

            _earthHitS = SOf(earth != null ? earth.position : Vector3.zero) - 7f;
            // Constant lateral offset from the wave axis: riding only changes the
            // along-axis coordinate, so the ship moves in one straight line.
            _rideOffset = ship.position - AxisPoint(SOf(ship.position));

            // Build the underground lab + tour + mission log BEFORE the first
            // narration so every line is logged from the very start.
            lab = gameObject.AddComponent<LabExperience>();
            lab.Build(this);

            _canStartMission = false;
            SetButtons(true, false, false);
            if (startButton != null) startButton.interactable = false;
            EnsureSkipButton();   // TEMP: global narration-skip test aid
            StartCoroutine(Intro());
        }

        // ------------------------------------------------------- TEMP SKIP --
        /// <summary>TEMPORARY test aid - REMOVE BEFORE FINAL DELIVERY. A
        /// permanent clone of the NEXT button docked one row lower; every
        /// click force-skips the running narration segment in ANY phase -
        /// voice stops, Busy-waits release, the mission flows on - and inside
        /// the lab tour it also jumps straight to the next step.</summary>
        GameObject _skipButton;

        void EnsureSkipButton()
        {
            if (nextButton == null) return;
            var src = nextButton.gameObject;
            var go = Instantiate(src, src.transform.parent);
            go.name = "Btn_SkipTemp";
            ((RectTransform)go.transform).anchoredPosition += new Vector2(0f, -62f);
            var btn = go.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(SkipCurrentNarration);
            btn.interactable = true;
            var label = go.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = "SKIP  ▶▶";
                var localized = label.GetComponent<LocalizedText>();
                if (localized != null)
                {
                    localized.english = "SKIP  ▶▶";
                    localized.chinese = "跳过  ▶▶";
                    localized.dutch = "SKIP  ▶▶";
                }
                if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(label);
            }
            var spot = go.GetComponent<UiSpotlightTarget>();
            if (spot != null) spot.id = "skip-temp";
            go.SetActive(true);
            _skipButton = go;
        }

        /// <summary>TEMPORARY: one click = drop the running narration and let
        /// the mission continue immediately (test aid - remove with the
        /// button before shipping).</summary>
        public void SkipCurrentNarration()
        {
            if (subtitles != null) subtitles.ResetTransport();
            if (_phase == Phase.InLab && lab != null) lab.SkipStep();
            else if (_phase == Phase.EarthPause) ContinueMission();
        }

        /// <summary>Opening monologue: who Rick is, where the user is, what
        /// gravity is (the bedsheet analogy they are literally looking at) and
        /// how to call Rick later - the avatar flashes while it is mentioned.</summary>
        System.Collections.IEnumerator Intro()
        {
            Narrate("Rick: Hi, I'm Rick - your AI guide for today's lesson on gravity, " +
                    "gravitational waves and how the Einstein Telescope detects them. " +
                    "You're aboard our research ship, close to two black holes about to merge. " +
                    "You can pause me - or the whole mission - any time with the transport " +
                    "button at the lower left.",
                    "Rick：你好，我是 Rick，今天的 AI 讲解助手。我们今天循序渐进地了解引力、" +
                    "引力波，以及爱因斯坦望远镜如何探测它们。你现在在一艘科研飞船里，" +
                    "就在两个即将合并的黑洞附近。左下角的播放键可以随时暂停我的讲解，" +
                    "或者暂停整个任务。",
                    "Rick: Hoi, ik ben Rick - je AI-gids voor de les van vandaag over " +
                    "zwaartekracht, zwaartekrachtgolven en hoe de Einstein Telescope ze meet. " +
                    "Je zit in ons onderzoeksschip, vlak bij twee zwarte gaten die gaan samensmelten. " +
                    "Met de knop linksonder pauzeer je mij - of de hele missie - op elk moment.");
            while (subtitles != null && subtitles.Busy) yield return null;

            Narrate("Rick: First: gravity. Imagine a stretched bedsheet with a heavy ball " +
                    "in the middle. The sheet bends, and a marble rolls toward the dip. " +
                    "That picture is only an analogy - space is not a cloth - but it helps " +
                    "show how mass changes the shape of spacetime and guides the paths of " +
                    "nearby objects. Look at the model beneath the black holes.",
                    "Rick：先说引力。想象一张绷紧的床单，中间放一个很重的球，床单会弯下去，" +
                    "小弹珠就会滚向凹陷处。这只是比喻——空间并不是真的床单——但它能帮助我们" +
                    "理解：质量会改变时空的形状，附近物体的运动路径也会随之改变。看看黑洞下方的模型。",
                    "Rick: Eerst: zwaartekracht. Denk aan een strak laken met een zware bal " +
                    "in het midden. Het laken buigt en een knikker rolt naar de kuil. Het is " +
                    "maar een vergelijking - ruimte is geen laken - maar zo zie je hoe massa " +
                    "de ruimtetijd vervormt en de beweging van nabije voorwerpen beïnvloedt. " +
                    "Kijk naar het model onder de zwarte gaten.");
            while (subtitles != null && subtitles.Busy) yield return null;

            Narrate("Rick: When heavy objects accelerate - like these two black holes " +
                    "orbiting and colliding - they send tiny ripples through spacetime. " +
                    "Those ripples are gravitational waves. You can call me any time by " +
                    "tapping the RICK face at the lower right - or just ask out loud " +
                    "with the microphone button in my chat.",
                    "Rick：当很重的天体加速运动时——比如这两个黑洞互相绕转并合并——它们会把" +
                    "细微的涟漪传遍时空，这就是引力波。想随时问我问题，就点右下角 RICK 的头像，" +
                    "也可以点开对话框里的话筒，直接说话提问。",
                    "Rick: Als zware objecten versnellen - zoals deze zwarte gaten die om " +
                    "elkaar draaien en botsen - sturen ze kleine rimpelingen door de " +
                    "ruimtetijd. Dat zijn zwaartekrachtgolven. Je kunt me altijd iets " +
                    "vragen via het Rick-gezicht rechtsonder - of stel je vraag hardop " +
                    "via de microfoonknop in mijn chat.");
            yield return StartCoroutine(SpotlightAfterNarration("rick-avatar", 4f));

            Narrate("Rick: Ahead of us - seen side-on - the pair is about to crash. " +
                    "That collision sends ripples through space itself. Press " +
                    "START MISSION when you are ready.",
                    "Rick：正前方侧面视角，这对黑洞即将相撞，碰撞会在空间中激起涟漪。" +
                    "准备好后点「开始任务」。",
                    "Rick: Recht voor ons - van opzij - gaan ze bijna botsen. Druk op " +
                    "MISSIE STARTEN als je klaar bent.");
            yield return StartCoroutine(SpotlightAfterNarration("start", 0f,
                () =>
                {
                    _canStartMission = true;
                    if (startButton != null) startButton.interactable = true;
                }));
        }

        /// <summary>Light a control only AFTER its narration has fully played.
        /// Optionally run a callback when the user can safely interact. `alsoId`
        /// lights a second control alongside the primary one (e.g. the CONTROLS
        /// rail button while the narration talks about the panel).</summary>
        internal Coroutine PromptAfterNarration(string id, float holdSeconds,
            System.Action onReady = null, string alsoId = null)
        {
            if (!Application.isPlaying)
            {
                onReady?.Invoke();
                return null;
            }
            if (_promptRoutine != null) StopCoroutine(_promptRoutine);
            _promptRoutine = StartCoroutine(
                SpotlightAfterNarration(id, holdSeconds, onReady, alsoId));
            return _promptRoutine;
        }

        System.Collections.IEnumerator SpotlightAfterNarration(string id, float holdSeconds,
            System.Action onReady = null, string alsoId = null)
        {
            while (subtitles != null && subtitles.Busy) yield return null;
            UiSpotlightTarget.Show(id);
            if (!string.IsNullOrEmpty(alsoId)) UiSpotlightTarget.ShowAdditional(alsoId);
            onReady?.Invoke();
            if (holdSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(holdSeconds);
                UiSpotlightTarget.Clear();
            }
            _promptRoutine = null;
        }

        float SOf(Vector3 p) => Vector3.Dot(p - model.Origin, model.PropagationDirection);
        Vector3 AxisPoint(float s) => model.Origin + model.PropagationDirection * s;

        public void Advance()
        {
            if (_phase == Phase.Waiting && _canStartMission) StartMission();
            else if (_phase == Phase.EarthPause) ContinueMission();
            else if (_phase == Phase.InLab && lab != null) lab.NextStep();
            else if (_phase == Phase.Done) RestartMission();
        }

        /// <summary>START: release the simulation. The ship does not move - it watches
        /// the whole inspiral and merger from its parked 90-degree vantage point.
        /// Any leftover preview state (clock / pose) is cleared first: the journey
        /// always begins at t=0 from the parked pose, not from a preview's time.</summary>
        public void StartMission()
        {
            _preview = false;
            if (subtitles != null) subtitles.ResetTransport();
            if (!_poseCaptured) CaptureInitialPose();
            ship.SetPositionAndRotation(_initialShipPos, _initialShipRot);
            _rideOffset = ship.position - AxisPoint(SOf(ship.position));
            _phase = Phase.WatchingMerger;
            simulation.RestartAt(0f, true);
            Narrate("Rick: Simulation released. Stay put and watch them spiral inward, " +
                    "faster and faster, until they collide. We only move once the " +
                    "wave is born.",
                    "Rick：模拟开始了。留在原地看它们越转越快，直到撞在一起。" +
                    "引力波诞生之后我们才动身。",
                    "Rick: Simulatie gestart. Blijf staan en zie ze sneller en sneller " +
                    "naar elkaar spiraliseren tot ze botsen. We bewegen pas als de " +
                    "golf er is.");
            SetButtons(false, false, false);
            // Do not pulse the persistent transport button unless the narration
            // explicitly asks the user to press it.
        }

        /// <summary>CONTINUE at the Earth stop: FIRST the wave freezes and Earth
        /// snaps back to its rest shape (seek the clock past the wave's transit
        /// at Earth so every strain reads ~0), THEN the ship dives. The whole
        /// descent runs with the simulation paused - nothing wobbles while the
        /// user sinks into the crust.</summary>
        public void ContinueMission()
        {
            if (_phase != Phase.EarthPause) return;
            _preview = false;
            SettleWaveBeforeDive();
            _phase = Phase.Diving;
            Narrate("Rick: Hold on - we are diving into the Earth. Deep under our feet " +
                    "sits the lab where the Einstein Telescope detects the wave. " +
                    "Following me down.",
                    "Rick：坐稳——我们要潜进地球内部了。在我们脚下的深处有一间实验室，" +
                    "爱因斯坦望远镜就在那里探测引力波。跟我下去。",
                    "Rick: Hou je vast - we duiken de aarde in. Diep onder onze voeten " +
                    "ligt het lab waar de Einstein Telescope de golf detecteert. " +
                    "Volg me naar beneden.");
            SetButtons(false, false, false);
            UiSpotlightTarget.Clear();
            if (lab != null) lab.BeginDescent();
            else OnLabArrived(); // safety: never trap the mission without the lab
        }

        /// <summary>Freeze the world for the descent. The clock is parked a few
        /// seconds past the wave's transit at Earth so every strain reads ~0
        /// (Earth deformer, spacetime sheet, wavefront visuals and the merged
        /// black-hole pair all settle), then the Earth visuals are snapped back
        /// to rest scale and switched off. The simulation stays paused for the
        /// whole dive - a Resume/Pause press cannot thaw it mid-descent.</summary>
        void SettleWaveBeforeDive()
        {
            float settle = 0f;
            if (model != null)
            {
                float v = model.parameters != null
                    ? Mathf.Max(model.parameters.visualizationSpeedMps, 1e-4f) : 1f;
                // StrainAt is EXACTLY zero once tau >= 8*ringdown, so park past
                // that threshold - Earth, the sheet and the wavefront all read
                // a clean zero, not merely "small".
                float tail = model.parameters != null
                    ? Mathf.Max(8f * model.parameters.ringdownSeconds,
                                model.parameters.packetDurationSeconds) + 1f
                    : 30f;
                settle = model.emissionDelay + tail;
                if (earth != null)
                    settle += model.PropagationDistance(earth.position) / v;
            }
            simulation.RestartAt(settle, false); // parked, reset listeners notified
            if (earth == null) return;
            foreach (var d in earth.GetComponentsInChildren<EarthDeformer>(true))
                d.RestoreRest();
        }

        /// <summary>Opposite of <see cref="SettleWaveBeforeDive"/>: wake the Earth
        /// visuals for a fresh run of the journey.</summary>
        void WakeEarthVisuals()
        {
            if (earth == null) return;
            foreach (var d in earth.GetComponentsInChildren<EarthDeformer>(true))
                d.enabled = true;
        }

        /// <summary>Descent finished - the ship is parked on the lab pad; start the
        /// guided step-by-step tour of the interferometer.</summary>
        internal void OnLabArrived()
        {
            _phase = Phase.InLab;
            if (nextButton != null) nextButton.gameObject.SetActive(true);
            if (lab != null) lab.StartTour();
        }

        /// <summary>The last tour step is done: hand control back and offer the
        /// RESTART MISSION button on the dock.</summary>
        internal void OnTourFinished()
        {
            _phase = Phase.Done;
            if (cockpit != null) cockpit.ResetLookTilt();
            Narrate("Rick: That is the whole detector - laser, splitter, arms, mirrors " +
                    "and the flicker that betrays a passing wave. Wander around the lab " +
                    "as long as you like, or press RESTART MISSION to fly the story again.",
                    "Rick：探测器的原理讲完了——激光、分光镜、两臂、反射镜，还有出卖引力波" +
                    "行踪的那一点闪烁。实验室里随便逛，或者点「重新开始任务」完整重看。",
                    "Rick: Dat was de hele detector - laser, deler, armen, spiegels en " +
                    "het flikkeren dat een voorbije golf verraadt. Kijk rustig rond in " +
                    "het lab, of druk op MISSIE HERSTARTEN voor de hele reis opnieuw.");
            SetButtons(false, false, true);
            if (restartButton != null) restartButton.interactable = false;
            if (nextButton != null) nextButton.gameObject.SetActive(false);
            UiSpotlightTarget.Clear();
            PromptAfterNarration("restart", 0f,
                () => { if (restartButton != null) restartButton.interactable = true; });
        }

        /// <summary>Reset the journey in place: ship back at its parked start, the
        /// clock at zero paused, phase Waiting. No scene reload, no loss of the
        /// user's wave parameters or saved slots.</summary>
        public void RestartMission()
        {
            _preview = false;
            _phase = Phase.Waiting;
            _canStartMission = false;
            if (subtitles != null) subtitles.ResetTransport();
            ship.SetPositionAndRotation(_initialShipPos, _initialShipRot);
            _rideOffset = ship.position - AxisPoint(SOf(ship.position));
            simulation.RestartAt(0f, false);
            WakeEarthVisuals();
            if (lab != null) lab.ResetTour();
            Narrate("Rick: Welcome aboard. Ahead of us - seen side-on - two black holes " +
                    "are about to crash into each other. That collision sends ripples " +
                    "through space itself: gravitational waves. Press START MISSION " +
                    "when you are ready.",
                    "Rick：欢迎上船。前方侧面视角有一对黑洞即将相撞，撞击会在空间中激起" +
                    "涟漪——这就是引力波。准备好后点「开始任务」。",
                    "Rick: Welkom aan boord. Voor ons - van opzij - staan twee zwarte " +
                    "gaten op het punt te botsen. Die klap stuurt rimpelingen door de " +
                    "ruimte zelf: zwaartekrachtgolven. Druk op MISSIE STARTEN.");
            SetButtons(true, false, false);
            if (startButton != null) startButton.interactable = false;
            UiSpotlightTarget.Clear();
            PromptAfterNarration("start", 0f,
                () =>
                {
                    _canStartMission = true;
                    if (startButton != null) startButton.interactable = true;
                });
        }

        // ------------------------------------------------- preview transport --
        /// <summary>Where the user is currently observing: binary before Earth,
        /// Earth at the Earth stop or while sliding there, the lab's demo
        /// interferometer once we have gone underground.</summary>
        Vector3 PreviewFocus()
        {
            switch (_phase)
            {
                case Phase.EarthPause:
                case Phase.SlidingEarth:
                case Phase.Riding:
                    return earth != null ? earth.position : binary.position;
                case Phase.Diving:
                case Phase.EnteringLab:
                case Phase.InLab:
                case Phase.Done:
                    return lab != null && lab.Ready
                        ? lab.FocusPoint
                        : (ligo != null ? ligo.position : binary.position);
                default:
                    return binary != null ? binary.position : ship.position;
            }
        }

        /// <summary>Replay CURRENT WAVE at the current viewpoint: an experiment
        /// preview, NOT the mission journey. The preview clock starts one
        /// displayed second before the wave reaches the observed target; the
        /// ship does not move and the mission phase is untouched.</summary>
        public void ReplayCurrentWave()
        {
            if (!CanPilot) return; // mid-transit: never freeze the journey
            float start = PreviewStartTime();
            _preview = true;
            simulation.RestartAt(start, true);
        }

        /// <summary>Reset returns the preview to the same arrival-relative start
        /// time, paused - ready to scrub or resume by hand.</summary>
        public void ResetCurrentWave()
        {
            if (!CanPilot) return; // mid-transit: never freeze the journey
            float start = PreviewStartTime();
            _preview = true;
            simulation.RestartAt(start, false);
        }

        /// <summary>Lab tour variant: land the wave on an explicit focus point -
        /// the step that shows the wave arriving replays it at the demo bench.</summary>
        public void ReplayWaveAtFocus(Vector3 focus)
        {
            float v = model != null && model.parameters != null
                ? Mathf.Max(model.parameters.visualizationSpeedMps, 1e-4f) : 1f;
            float delay = model != null ? model.emissionDelay : 0f;
            float dist = model != null ? model.PropagationDistance(focus) : 0f;
            _preview = true;
            simulation.RestartAt(Mathf.Max(0f, delay + dist / v - 1f), true);
        }

        float PreviewStartTime()
        {
            Vector3 focus = PreviewFocus();
            float v = model != null && model.parameters != null
                ? Mathf.Max(model.parameters.visualizationSpeedMps, 1e-4f) : 1f;
            float delay = model != null ? model.emissionDelay : 0f;
            float dist = model != null ? model.PropagationDistance(focus) : 0f;
            return Mathf.Max(0f, delay + dist / v - 1f);
        }

        /// <summary>The single Play/Pause action: at the parked start it begins
        /// the mission; afterwards it is a MASTER pause - narration (voice +
        /// page clock) and the simulation clock freeze and resume together,
        /// even in the gaps between spoken lines when nothing is on air.</summary>
        public void TogglePlayback()
        {
            // Mid-descent everything stays frozen - the transport button must not
            // thaw the wave or Earth while the ship sinks into the crust.
            if (_phase == Phase.Diving || _phase == Phase.EnteringLab) return;

            if (_phase == Phase.Waiting)
            {
                // Priority while parked: a held narration resumes first, a live
                // narration pauses, a parked wave preview toggles its own clock,
                // and only an idle start actually begins the mission. The old
                // ordering left the button dead during a Waiting preview.
                if (subtitles != null && subtitles.Paused)
                {
                    subtitles.TogglePause();
                    return;
                }
                if (subtitles != null && subtitles.Busy)
                {
                    subtitles.TogglePause();
                    return;
                }
                if (_preview)
                {
                    simulation.TogglePlayPause();
                    return;
                }
                if (_canStartMission)
                {
                    StartMission();
                    return;
                }
                return;
            }

            // Master pause: freeze the narration presenter and the simulation
            // clock as one unit. The sim only pauses/resumes in step with the
            // narration pause flag, so a paused wave can never thaw silently
            // in the lab where the transport must leave the earth settled.
            if (subtitles != null) subtitles.TogglePause();
            bool narrPaused = subtitles != null && subtitles.Paused;
            if (narrPaused != !simulation.IsPlaying)
                simulation.TogglePlayPause();
        }

        void Update()
        {
            // Paused simulation or wave preview: all choreography - ride and
            // slides alike - freezes exactly where it is.
            if (!simulation.IsPlaying || _preview) return;
            float s = model.WavefrontDistance(simulation.SimulationTime);
            switch (_phase)
            {
                case Phase.WatchingMerger:
                {
                    // Ship stays parked. Only after the holes have coalesced AND the
                    // burst front has visibly left the binary do we start moving.
                    bool merged = simulation.SimulationTime >= model.parameters.inspiralDurationSeconds;
                    if (merged && s >= SOf(ship.position) + departLeadMeters)
                    {
                        _phase = Phase.Riding;
                        Narrate("Rick: The wave is out - and we ride along with it. We " +
                                "stay just behind the front so you can watch space " +
                                "stretch and squeeze all the way to Earth.",
                                "Rick：引力波出现了——我们跟着它一起走。飞船始终跟在波前" +
                                "后面一点点，你能一路看着空间拉伸、挤压，直到地球。",
                                "Rick: De golf is er - en we surfen mee. We blijven net " +
                                "achter het golffront zodat je de ruimte helemaal tot " +
                                "de Aarde kunt zien uitrekken en samendrukken.");
                    }
                    break;
                }

                case Phase.Riding:
                {
                    RideBehindWavefront(s);
                    if (s >= _earthHitS)
                    {
                        _phase = Phase.SlidingEarth;
                        Narrate("Rick: The wave just reached Earth. Sliding into a " +
                                "side-on view of the whole planet.",
                                "Rick：引力波刚到地球。飞船滑入能看到整个地球的侧面观察位。",
                                "Rick: De golf heeft de Aarde bereikt. We schuiven naar " +
                                "een volledig zijaanzicht van de planeet.");
                    }
                    break;
                }

                case Phase.SlidingEarth:
                {
                    if (earth == null) { _phase = Phase.EarthPause; break; }
                    if (SlideTo(earth.position + earthWatchOffset))
                    {
                        _phase = Phase.EarthPause;
                        SetButtons(false, true, false);
                        if (continueButton != null) continueButton.interactable = false;
                        StartCoroutine(EarthPauseIntro());
                    }
                    break;
                }

                // Diving / EnteringLab: the lab's descent coroutine owns the ship.
                // InLab: free flight + the guided tour; no further choreography.
            }
        }

        /// <summary>EarthPause intro in two beats: the CONTROLS rail lights the
        /// moment Rick STARTS talking about the panel - not after the whole
        /// invitation has played - then CONTINUE joins the glow and unlocks
        /// once the invitation is complete.</summary>
        System.Collections.IEnumerator EarthPauseIntro()
        {
            Narrate("Rick: Parked side-on - the whole planet is in view.",
                    "Rick：已停在侧面观察位，地球全貌可见。",
                    "Rick: Geparkeerd opzij - de hele planeet is zichtbaar.");
            while (subtitles != null && subtitles.Busy) yield return null;
            if (_phase != Phase.EarthPause) yield break;

            // Ring appears WITH the sentence about the panel, and stays lit for
            // the whole time Rick explains it.
            UiSpotlightTarget.Show("controls");
            Narrate("Rick: Play with the CONTROLS panel if you like: change the " +
                    "amplitude, frequency or exaggeration and watch the wave " +
                    "through Earth respond. Press CONTINUE and we dive " +
                    "underground to see how the detector feels it.",
                    "Rick：可以先玩一下「控制台」：调调振幅、频率或夸张度，看穿过地球的波" +
                    "怎么变化。玩好点「继续」，我们潜入地下看探测器是怎么感觉到它的。",
                    "Rick: Speel gerust met het BESTURING-paneel: verander amplitude, " +
                    "frequentie of overdrijving en kijk hoe de golf door de Aarde " +
                    "reageert. Druk op DOORGAAN en we duiken ondergronds om te zien " +
                    "hoe de detector hem voelt.");
            while (subtitles != null && subtitles.Busy) yield return null;
            if (_phase != Phase.EarthPause) yield break;

            // The invitation is complete: CONTINUE joins the glow and unlocks.
            UiSpotlightTarget.ShowAdditional("continue");
            if (continueButton != null) continueButton.interactable = true;
            yield return new WaitForSecondsRealtime(6f);
            UiSpotlightTarget.Clear();
        }

        /// <summary>Pure lateral translation along the wave axis, a fixed distance
        /// behind the wavefront. Rotation is never touched - heading stays at 90
        /// degrees to the wave line for the entire mission.</summary>
        void RideBehindWavefront(float s)
        {
            float lagged = Mathf.Max(s - rideLagBehindWavefront, 0f);
            Vector3 target = AxisPoint(lagged) + _rideOffset;
            ship.position = Vector3.Lerp(ship.position, target, Time.deltaTime * rideBlendSpeed);
        }

        /// <summary>Position-only glide to a spot. Heading never changes.</summary>
        bool SlideTo(Vector3 spot)
        {
            ship.position = Vector3.Lerp(ship.position, spot, Time.deltaTime * slideBlendSpeed);
            return (ship.position - spot).sqrMagnitude < 0.5f;
        }

        /// <summary>Three-language narration through the shared subtitle presenter;
        /// internal so the lab tour can speak through the same channel (and land
        /// in the mission log automatically).</summary>
        internal void Narrate(string english, string chinese, string dutch = null)
        {
            string chosen = ExperienceSettings.IsChinese ? chinese
                : ExperienceSettings.IsDutch && dutch != null ? dutch : english;
            if (subtitles != null)
            {
                if (ExperienceSettings.IsChinese && subtitles.text != null)
                    LocalizedText.UseChineseFont(subtitles.text);
                subtitles.ShowNarration(chosen);
            }
            else
            {
                // Legacy fallback - only when no subtitle service is wired.
                if (narrationText != null)
                {
                    if (ExperienceSettings.IsChinese)
                        LocalizedText.UseChineseFont(narrationText);
                    narrationText.text = chosen;
                }
                if (voice != null) voice.Speak(chosen);
            }
            // When the subtitle presenter is wired it owns the voice: an
            // utterance arriving during an AI answer is held until the answer
            // finishes instead of cutting it off.
        }

        void SetButtons(bool start, bool cont, bool restart)
        {
            if (startButton != null) startButton.gameObject.SetActive(start);
            if (continueButton != null) continueButton.gameObject.SetActive(cont);
            if (restartButton != null) restartButton.gameObject.SetActive(restart);
        }
    }
}
