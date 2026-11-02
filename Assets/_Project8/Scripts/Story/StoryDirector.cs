using UnityEngine;
using UnityEngine.UI;
using Project8.Core;
using Project8.Interaction;
using Project8.UI;

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

            SetButtons(true, false, false);
            StartCoroutine(Intro());
        }

        /// <summary>Opening monologue: who Rick is, where the user is, what
        /// gravity is (the bedsheet analogy they are literally looking at) and
        /// how to call Rick later - the avatar flashes while it is mentioned.</summary>
        System.Collections.IEnumerator Intro()
        {
            Narrate("Rick: Hi - I'm Rick, your AI guide today. You're sitting in a " +
                    "spaceship drifting near two black holes that are about to merge.",
                    "Rick：你好，我是 Rick，今天的 AI 讲解助手。你现在正坐在一艘宇宙飞船里，" +
                    "漂浮在两个即将相撞的黑洞附近。",
                    "Rick: Hoi - ik ben Rick, je AI-gids vandaag. Je zit in een " +
                    "ruimteschip vlak bij twee zwarte gaten die gaan samensmelten.");
            while (subtitles != null && subtitles.Busy) yield return null;

            Narrate("Rick: What is gravity? Imagine a bedsheet - drop a ball on it and " +
                    "the sheet dips, so smaller things roll toward it. Stars do the same " +
                    "thing to space itself: that dip IS gravity. You can see it on the " +
                    "sheet right below the black holes.",
                    "Rick：什么是引力？想象一张床单——放个球上去，床单被压出一个坑，周围的" +
                    "小东西就会往坑里滚。星球对时空做的事一模一样：压弯时空就是引力。" +
                    "看黑洞脚下那张床单，就是这个效果。",
                    "Rick: Wat is zwaartekracht? Denk aan een laken - leg er een bal op " +
                    "en het zakt in, dus kleinere dingen rollen ernaartoe. Sterren doen " +
                    "datzelfde met de ruimte zelf. Kijk naar het laken onder de zwarte gaten.");
            while (subtitles != null && subtitles.Busy) yield return null;

            Narrate("Rick: When these two monsters spiral and collide, the sheet shakes " +
                    "hard - those ripples racing outward are gravitational waves. And " +
                    "whenever you want to ask me anything, tap the glowing RICK button " +
                    "in your lower right.",
                    "Rick：当这两个大家伙互绕相撞时，床单会剧烈抖动——向外扩散的波纹就是" +
                    "引力波。想问我任何问题，随时点右下角的 RICK 按钮呼叫我。",
                    "Rick: Als deze twee reuzen spiraliseren en botsen, schudt het laken - " +
                    "die rimpelingen zijn zwaartekrachtgolven. Wil je me iets vragen? " +
                    "Tik op de RICK-knop rechtsonder.");
            UiSpotlightTarget.Show("rick-avatar");
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 4f) yield return null;
            UiSpotlightTarget.Clear();

            Narrate("Rick: Ahead of us - seen side-on - the pair is about to crash. " +
                    "That collision sends ripples through space itself. Press " +
                    "START MISSION when you are ready.",
                    "Rick：正前方侧面视角，这对黑洞即将相撞，碰撞会在空间中激起涟漪。" +
                    "准备好后点「开始任务」。",
                    "Rick: Recht voor ons - van opzij - gaan ze bijna botsen. Druk op " +
                    "MISSIE STARTEN als je klaar bent.");
            StartCoroutine(SpotlightAfter("start", 0f));
        }

        /// <summary>Light a control only AFTER the narration that mentions it has
        /// fully played - not while Rick is still explaining. holdSeconds <= 0
        /// keeps it lit until the next cue replaces it.</summary>
        System.Collections.IEnumerator SpotlightAfter(string id, float holdSeconds)
        {
            while (subtitles != null && subtitles.Busy) yield return null;
            UiSpotlightTarget.Show(id);
            if (holdSeconds > 0f)
            {
                yield return new WaitForSeconds(holdSeconds);
                UiSpotlightTarget.Clear();
            }
        }

        float SOf(Vector3 p) => Vector3.Dot(p - model.Origin, model.PropagationDirection);
        Vector3 AxisPoint(float s) => model.Origin + model.PropagationDirection * s;

        public void Advance()
        {
            if (_phase == Phase.Waiting) StartMission();
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
            StartCoroutine(SpotlightAfter("play", 8f));
        }

        /// <summary>CONTINUE at the Earth stop: the ship dives toward the planet and
        /// sinks into the crust - the underground lab takes over from there.</summary>
        public void ContinueMission()
        {
            if (_phase != Phase.EarthPause) return;
            _preview = false;         // leave any wave preview and resume travel
            simulation.Play();
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

        /// <summary>Descent finished - the ship is parked on the lab pad; start the
        /// guided step-by-step tour of the interferometer.</summary>
        internal void OnLabArrived()
        {
            _phase = Phase.InLab;
            if (lab != null) lab.StartTour();
            if (nextButton != null) nextButton.gameObject.SetActive(true);
            UiSpotlightTarget.Show("lab-next");
        }

        /// <summary>The last tour step is done: hand control back and offer the
        /// RESTART MISSION button on the dock.</summary>
        internal void OnTourFinished()
        {
            _phase = Phase.Done;
            Narrate("Rick: That is the whole detector - laser, splitter, arms, mirrors " +
                    "and the flicker that betrays a passing wave. Wander around the lab " +
                    "as long as you like, or press RESTART MISSION to fly the story again.",
                    "Rick：探测器的原理讲完了——激光、分光镜、两臂、反射镜，还有出卖引力波" +
                    "行踪的那一点闪烁。实验室里随便逛，或者点「重新开始任务」完整重看。",
                    "Rick: Dat was de hele detector - laser, deler, armen, spiegels en " +
                    "het flikkeren dat een voorbije golf verraadt. Kijk rustig rond in " +
                    "het lab, of druk op MISSIE HERSTARTEN voor de hele reis opnieuw.");
            SetButtons(false, false, true);
            if (nextButton != null) nextButton.gameObject.SetActive(false);
            UiSpotlightTarget.Clear();
        }

        /// <summary>Reset the journey in place: ship back at its parked start, the
        /// clock at zero paused, phase Waiting. No scene reload, no loss of the
        /// user's wave parameters or saved slots.</summary>
        public void RestartMission()
        {
            _preview = false;
            _phase = Phase.Waiting;
            ship.SetPositionAndRotation(_initialShipPos, _initialShipRot);
            _rideOffset = ship.position - AxisPoint(SOf(ship.position));
            simulation.RestartAt(0f, false);
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
        /// the mission (simulation + story together); afterwards it pauses and
        /// resumes the same clock without restarting anything.</summary>
        public void TogglePlayback()
        {
            if (_phase == Phase.Waiting && !_preview) StartMission();
            else simulation.TogglePlayPause();
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
                        Narrate("Rick: Parked side-on - the whole planet is in view. " +
                                "Play with the CONTROLS panel if you like: change the " +
                                "amplitude, frequency or exaggeration and watch the " +
                                "wave through Earth respond. Press CONTINUE and we dive " +
                                "underground to see how the detector feels it.",
                                "Rick：已停在侧面观察位，地球全貌可见。可以先玩一下「控制台」：" +
                                "调调振幅、频率或夸张度，看穿过地球的波怎么变化。玩好点「继续」，" +
                                "我们潜入地下看探测器是怎么感觉到它的。",
                                "Rick: Geparkeerd opzij - de hele planeet is zichtbaar. " +
                                "Speel gerust met het BESTURING-paneel: verander " +
                                "amplitude, frequentie of overdrijving en kijk hoe de " +
                                "golf door de Aarde reageert. Druk op DOORGAAN en we " +
                                "duiken ondergronds om te zien hoe de detector hem voelt.");
                        SetButtons(false, true, false);
                        UiSpotlightTarget.Show("continue");
                    }
                    break;
                }

                // Diving / EnteringLab: the lab's descent coroutine owns the ship.
                // InLab: free flight + the guided tour; no further choreography.
            }
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
