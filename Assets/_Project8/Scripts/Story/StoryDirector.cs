using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
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
        public Transform ligo;
        public CockpitControls cockpit;
        public RickVoice voice;

        [Header("UI wiring")]
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
        public Vector3 ligoWatchOffset = new Vector3(0f, 2.5f, -12f);

        enum Phase { Waiting, WatchingMerger, Riding, SlidingEarth, EarthPause,
                     RidingLigo, SlidingLigo, Done }
        Phase _phase = Phase.Waiting;
        public bool CanPilot => _phase == Phase.Waiting || _phase == Phase.EarthPause ||
                                _phase == Phase.Done;

        float _earthHitS, _ligoHitS;
        Vector3 _rideOffset;

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
        /// the whole inspiral and merger from its parked 90-degree vantage point.</summary>
        public void StartMission()
        {
            _phase = Phase.WatchingMerger;
            simulation.Play();
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
            _phase = Phase.RidingLigo;
            _rideOffset = ship.position - AxisPoint(SOf(ship.position));
            Narrate("Rick: Onward - the wave does not stop at Earth. It rolls on toward a " +
                    "laser detector further down the line.",
                    "Rick：继续前进——波不会在地球停下，它继续向前，前往下游的一台激光探测器。",
                    "Rick: Verder - de golf stopt niet bij de Aarde. Ze rolt door naar een " +
                    "laserdetector verderop.");
            SetButtons(false, false, false);
        }

        /// <summary>Reload the scene: binary restored, simulation back to t = 0.</summary>
        public void RestartMission()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void Update()
        {
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
                        Narrate("Rick: Past Earth the wave reaches a laser interferometer. " +
                                "Parking beside it.",
                                "Rick：波穿过地球后抵达激光干涉仪。飞船正停到它旁边。",
                                "Rick: Voorbij de Aarde bereikt de golf een " +
                                "laserinterferometer. We parkeren ernaast.");
                    }
                    break;
                }

                case Phase.SlidingEarth:
                {
                    if (earth == null) { _phase = Phase.EarthPause; break; }
                    if (SlideTo(earth.position + earthWatchOffset))
                    {
                        _phase = Phase.EarthPause;
                        if (cockpit != null) cockpit.OpenMission();
                        Narrate("Rick: Parked 90 degrees off the wave path - the whole planet " +
                                "is in view. Try the CONTROLS panel: change the amplitude, " +
                                "frequency or exaggeration and watch how the wave through " +
                                "Earth responds. Press CONTINUE when you want to fly on to " +
                                "the laser detector.",
                                "Rick：已停在波路径侧面 90 度位置，地球全貌可见。现在可以打开「控制台」面板：" +
                                "调整振幅、频率或夸张度，观察穿过地球的波如何变化。玩好了点「继续」，前往激光探测器。",
                                "Rick: Geparkeerd op 90 graden van het golfpad - de hele planeet " +
                                "is zichtbaar. Probeer het BESTURING-paneel: verander amplitude, " +
                                "frequentie of overdrijving en kijk hoe de golf door de Aarde " +
                                "reageert. Druk op DOORGAAN om naar de laserdetector te vliegen.");
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
            if (cockpit != null) cockpit.OpenMission();
            Narrate("Rick: Real gravitational waves reach Earth at a strain of only ~1e-21 - " +
                    "far too small to see. This LIGO-style detector measures them with two " +
                    "perpendicular laser arms: the wave stretches one arm while it squeezes " +
                    "the other, shifting the interference pattern. Watch the arms flex as " +
                    "the wave passes. Press RESTART MISSION to fly the whole sequence again.",
                    "Rick：真实引力波到达地球时应变仅约 10⁻²¹，肉眼完全无法看见。这台 LIGO 式" +
                    "干涉仪用两条垂直激光臂来测量：波让一条臂拉伸的同时另一条臂收缩，" +
                    "干涉图样随之改变。观察波经过时两臂的伸缩。点击「重新开始任务」可完整重看。",
                    "Rick: Echte zwaartekrachtgolven bereiken de Aarde met een amplitude van " +
                    "slechts ~1e-21 - veel te klein om te zien. Deze LIGO-achtige detector " +
                    "meet ze met twee loodrechte laserarmen: de golf rekt de ene arm uit en " +
                    "perst de andere samen, waardoor het interferentiepatroon verschuift. " +
                    "Druk op MISSIE HERSTARTEN om alles opnieuw te zien.");
            SetButtons(false, false, true);
        }

        void Narrate(string english, string chinese, string dutch = null)
        {
            if (narrationText == null) return;
            if (ExperienceSettings.IsChinese)
            {
                LocalizedText.UseChineseFont(narrationText);
                narrationText.text = chinese;
            }
            else
            {
                narrationText.text = ExperienceSettings.IsDutch && dutch != null ? dutch : english;
            }
            if (voice != null) voice.Speak(narrationText.text);
        }

        void SetButtons(bool start, bool cont, bool restart)
        {
            if (startButton != null) startButton.gameObject.SetActive(start);
            if (continueButton != null) continueButton.gameObject.SetActive(cont);
            if (restartButton != null) restartButton.gameObject.SetActive(restart);
        }
    }
}
