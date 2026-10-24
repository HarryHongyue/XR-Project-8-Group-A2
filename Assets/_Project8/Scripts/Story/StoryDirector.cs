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
    ///   6. RidingLigo     - lateral cruise to the interferometer behind Earth
    ///   7. SlidingLigo    - slide into the detector observation spot
    ///   8. Done           - LIGO explanation + RESTART MISSION
    /// </summary>
    public class StoryDirector : MonoBehaviour
    {
        public SimulationController simulation;
        public GravitationalWaveModel model;
        public Transform ship;
        public Transform earth;
        public Transform binary;
        [Tooltip("The Einstein Telescope observatory root (final mission stop).")]
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
        [Tooltip("Elevated oblique overview of the horizontal ET triangle.")]
        public Vector3 ligoWatchOffset = new Vector3(0f, 11f, -14f);

        enum Phase { Waiting, WatchingMerger, Riding, SlidingEarth, EarthPause,
                     RidingLigo, SlidingLigo, Done }
        Phase _phase = Phase.Waiting;
        public bool CanPilot => _phase == Phase.Waiting || _phase == Phase.EarthPause ||
                                _phase == Phase.Done;
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

        float _earthHitS, _ligoHitS;
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
            _ligoHitS = SOf(ligo != null ? ligo.position : Vector3.zero) - 10f;
            // Constant lateral offset from the wave axis: riding only changes the
            // along-axis coordinate, so the ship moves in one straight line.
            _rideOffset = ship.position - AxisPoint(SOf(ship.position));

            Narrate("Rick: Welcome aboard. Dead ahead - seen side-on - a pair of black holes " +
                    "is about to merge. Their collision will send ripples through spacetime " +
                    "itself: gravitational waves. Press START MISSION when you are ready.",
                    "Rick：欢迎登上飞船。正侧面方向上，一对黑洞即将合并，碰撞会在时空中激起引力波。准备好后点击「开始任务」。",
                    "Rick: Welkom aan boord. Recht voor ons - van opzij gezien - staan twee " +
                    "zwarte gaten op het punt samen te smelten. De botsing stuurt rimpelingen " +
                    "door de ruimtetijd: zwaartekrachtgolven. Druk op MISSIE STARTEN.");
            SetButtons(true, false, false);
        }

        float SOf(Vector3 p) => Vector3.Dot(p - model.Origin, model.PropagationDirection);
        Vector3 AxisPoint(float s) => model.Origin + model.PropagationDirection * s;

        public void Advance()
        {
            if (_phase == Phase.Waiting) StartMission();
            else if (_phase == Phase.EarthPause) ContinueMission();
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
            Narrate("Rick: Simulation released. Stay put - watch them spiral inward, faster " +
                    "and faster, until they collide. We only move once the wave is born.",
                    "Rick：模拟已启动。飞船保持不动——观察它们越转越快直到碰撞。引力波产生之后我们才会动身。",
                    "Rick: Simulatie vrijgegeven. Blijf staan - zie ze sneller en sneller " +
                    "naar elkaar spiraliseren tot ze botsen. We bewegen pas als de golf er is.");
            SetButtons(false, false, false);
        }

        /// <summary>CONTINUE at the Earth stop: leave Earth and ride on to the
        /// interferometer parked further down the wave line.</summary>
        public void ContinueMission()
        {
            if (_phase != Phase.EarthPause) return;
            _preview = false;         // leave any wave preview and resume travel
            simulation.Play();
            _phase = Phase.RidingLigo;
            _rideOffset = ship.position - AxisPoint(SOf(ship.position));
            Narrate("Rick: Onward - the wave does not stop at Earth. It rolls on toward a " +
                    "model of the Einstein Telescope further down the line.",
                    "Rick：继续前进——波不会在地球停下，它继续向前，前往下游的爱因斯坦望远镜模型。",
                    "Rick: Verder - de golf stopt niet bij de Aarde. Ze rolt door naar een " +
                    "model van de Einstein Telescope verderop.");
            SetButtons(false, false, false);
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
            Narrate("Rick: Welcome aboard. Dead ahead - seen side-on - a pair of black holes " +
                    "is about to merge. Their collision will send ripples through spacetime " +
                    "itself: gravitational waves. Press START MISSION when you are ready.",
                    "Rick：欢迎登上飞船。正侧面方向上，一对黑洞即将合并，碰撞会在时空中激起引力波。准备好后点击「开始任务」。",
                    "Rick: Welkom aan boord. Recht voor ons - van opzij gezien - staan twee " +
                    "zwarte gaten op het punt samen te smelten. De botsing stuurt rimpelingen " +
                    "door de ruimtetijd: zwaartekrachtgolven. Druk op MISSIE STARTEN.");
            SetButtons(true, false, false);
        }

        // ------------------------------------------------- preview transport --
        /// <summary>Where the user is currently observing: binary before Earth,
        /// Earth at the Earth stop or while sliding there, the telescope at the
        /// telescope leg or when done.</summary>
        Vector3 PreviewFocus()
        {
            switch (_phase)
            {
                case Phase.EarthPause:
                case Phase.SlidingEarth:
                case Phase.Riding:
                    return earth != null ? earth.position : binary.position;
                case Phase.RidingLigo:
                case Phase.SlidingLigo:
                case Phase.Done:
                    return ligo != null ? ligo.position : binary.position;
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
                        Narrate("Rick: The wave is out - and we move with it. We trail just " +
                                "behind the wavefront so you can watch space stretch and " +
                                "squeeze all the way to Earth.",
                                "Rick：引力波已经产生——我们开始跟随。飞船始终比波前慢一点点，" +
                                "你能一路看着空间拉伸挤压直到地球。",
                                "Rick: De golf is er - en we bewegen mee. We blijven net " +
                                "achter het golffront zodat je de ruimte de hele weg naar " +
                                "de Aarde ziet uitrekken en samendrukken.");
                    }
                    break;
                }

                case Phase.Riding:
                case Phase.RidingLigo:
                {
                    RideBehindWavefront(s);
                    if (_phase == Phase.Riding && s >= _earthHitS)
                    {
                        _phase = Phase.SlidingEarth;
                        Narrate("Rick: The wave just reached Earth. Sliding into a side-on " +
                                "view of the whole planet.",
                                "Rick：波刚刚抵达地球。飞船横向滑入能看全地球的侧面观察位。",
                                "Rick: De golf heeft de Aarde bereikt. We schuiven zijwaarts " +
                                "naar een volledig zijaanzicht van de planeet.");
                    }
                    else if (_phase == Phase.RidingLigo && s >= _ligoHitS)
                    {
                        _phase = Phase.SlidingLigo;
                        Narrate("Rick: Past Earth the wave reaches the Einstein Telescope " +
                                "observatory model. Parking above it for the overview.",
                                "Rick：波穿过地球后抵达爱因斯坦望远镜观测站模型。飞船正升到它上方俯视。",
                                "Rick: Voorbij de Aarde bereikt de golf het " +
                                "Einstein Telescope-observatoriummodel. We parkeren " +
                                "er boven voor het overzicht.");
                    }
                    break;
                }

                case Phase.SlidingEarth:
                {
                    if (earth == null) { _phase = Phase.EarthPause; break; }
                    if (SlideTo(earth.position + earthWatchOffset))
                    {
                        _phase = Phase.EarthPause;
                        Narrate("Rick: Parked 90 degrees off the wave path - the whole planet " +
                                "is in view. Try the CONTROLS panel: change the amplitude, " +
                                "frequency or exaggeration and watch how the wave through " +
                                "Earth responds. Press CONTINUE when you want to fly on to " +
                                "the Einstein Telescope model.",
                                "Rick：已停在波路径侧面 90 度位置，地球全貌可见。现在可以打开「控制台」面板：" +
                                "调整振幅、频率或夸张度，观察穿过地球的波如何变化。玩好了点「继续」，前往爱因斯坦望远镜模型。",
                                "Rick: Geparkeerd op 90 graden van het golfpad - de hele planeet " +
                                "is zichtbaar. Probeer het BESTURING-paneel: verander amplitude, " +
                                "frequentie of overdrijving en kijk hoe de golf door de Aarde " +
                                "reageert. Druk op DOORGAAN om naar het Einstein " +
                                "Telescope-model te vliegen.");
                        SetButtons(false, true, false);
                    }
                    break;
                }

                case Phase.SlidingLigo:
                {
                    if (ligo == null) { FinishMission(); break; }
                    if (SlideTo(ligo.position + ligoWatchOffset))
                        FinishMission();
                    break;
                }
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

        void FinishMission()
        {
            _phase = Phase.Done;
            Narrate("Rick: This is the Einstein Telescope model - three interferometers " +
                    "arranged in an equilateral triangle, each station's arms meeting at " +
                    "60 degrees, and each station nesting a low- and a high-frequency " +
                    "instrument. That is the real 2020 design baseline, shown as a " +
                    "teaching cutaway: 10 km sides drawn 12 metres wide, light slowed, " +
                    "mirror motion exaggerated, and the two lanes share one simplified " +
                    "strain - the real cavities, suspensions and noise curves are not " +
                    "simulated. A wave shifts each arm's round trip by a different " +
                    "amount, so every dark port reads differently. Pick D1, D2 or D3 in " +
                    "the ET INSPECTOR to look inside one station. Press RESTART MISSION " +
                    "to fly it again.",
                    "Rick：这是爱因斯坦望远镜（ET）模型——三个干涉仪排成等边三角形，每个" +
                    "台站两臂夹角 60 度，每个台站内嵌低频（LF）与高频（HF）两台仪器。这" +
                    "是真实的 2020 设计基线的教学剖面：10 公里边长画成 12 米宽，光被放慢、" +
                    "镜面运动被夸大，两条光路共用同一简化应变——真实的法布里-珀罗腔、悬挂" +
                    "系统和噪声曲线并未模拟。引力波使每条臂的往返光程变化不同，各暗端口" +
                    "读数因此不同。在 ET INSPECTOR 中选择 D1、D2、D3 查看单台站光路。" +
                    "点击「重新开始任务」可完整重看。",
                    "Rick: Dit is het Einstein Telescope-model - drie interferometers in " +
                    "een gelijkzijdige driehoek, elk station met armen onder 60 graden en " +
                    "elk station met een laag- en een hoogfrequent instrument. Dat is de " +
                    "echte ontwerpbasislijn uit 2020, getoond als leermodel: 10 km-zijden " +
                    "12 meter breed getekend, licht vertraagd, spiegelbeweging overdreven " +
                    "en beide banen delen dezelfde vereenvoudigde rek - de echte caviteiten, " +
                    "ophangingen en ruiscurves worden niet gesimuleerd. Een golf verandert " +
                    "elke retourtijd anders, dus elke donkere poort leest anders. Kies D1, " +
                    "D2 of D3 in de ET INSPECTOR om in een station te kijken. Druk op " +
                    "MISSIE HERSTARTEN om alles opnieuw te zien.");
            SetButtons(false, false, true);
        }

        void Narrate(string english, string chinese, string dutch = null)
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
