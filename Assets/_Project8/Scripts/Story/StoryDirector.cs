using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Project8.Core;
using Project8.Visualization;
using Project8.Interaction;
using Project8.UI;

namespace Project8.Story
{
    /// <summary>
    /// Drives the storyboard sequence of the Story scene:
    ///   1. spawn in the ship + intro (simulation paused, waits for START)
    ///   2. ship flies toward the binary, holes start spinning when sim resumes
    ///   3. pause + AI explanation, user presses CONTINUE
    ///   4. merger chirp - wave sweeps past the net
    ///   5. ship rides along the wavefront toward Earth
    ///   6. wave reaches Earth - exaggerated deformation
    ///   7. ship approaches Earth, narrator explains LIGO/laser interferometry
    ///   8. RESTART MISSION replays the whole sequence from scratch
    /// </summary>
    public class StoryDirector : MonoBehaviour
    {
        public SimulationController simulation;
        public GravitationalWaveModel model;
        public Transform ship;
        public Transform earth;
        public Transform binary;
        public CockpitControls cockpit;
        public Project8.UI.RickVoice voice;

        [Header("UI wiring")]
        public Text narrationText;
        public Button startButton;
        public Button continueButton;
        public Button restartButton;

        [Header("Choreography")]
        public Vector3 flyTarget = new Vector3(-7f, 1.3f, 10f);
        public float flySeconds = 7f;
        public float rideLagBehindWavefront = 1.5f;
        public float rideLateralOffset = 3f;
        public float rideLiftY = 3.2f;
        public Vector3 earthWatchOffset = new Vector3(-5f, 4.5f, -11f);

        enum Phase { Waiting, Flying, Explaining, Waves, RidingToEarth, ApproachingEarth, Done }
        Phase _phase = Phase.Waiting;
        public bool CanPilot => _phase == Phase.Waiting || _phase == Phase.Explaining || _phase == Phase.Done;
        Vector3 _flyFrom;
        float _flyT;
        float _earthHitS;
        Vector3 _rideOffset;

        void Start()
        {
            simulation.Pause();
            _earthHitS = SOf(earth != null ? earth.position : Vector3.back * 20f) - 7f;
            Narrate("Rick: Welcome aboard. Dead ahead a pair of black holes is about to merge. " +
                    "Their collision will send ripples through spacetime itself - gravitational waves. " +
                    "Press START MISSION when you are ready.",
                    "Rick：欢迎登上飞船。前方的双黑洞即将合并，碰撞会释放引力波。准备好后点击「开始任务」。",
                    "Rick: Welkom aan boord. Recht voor ons staan twee zwarte gaten op het punt " +
                    "samem te smelten. De botsing stuurt rimpelingen door de ruimtetijd zelf - " +
                    "zwaartekrachtgolven. Druk op MISSIE STARTEN als je er klaar voor bent.");
            SetButtons(true, false, false);
        }

        float SOf(Vector3 p) => Vector3.Dot(p - model.Origin, model.PropagationDirection);

        public void Advance()
        {
            if (_phase == Phase.Waiting) StartMission();
            else if (_phase == Phase.Explaining) ContinueMission();
            else if (_phase == Phase.Done) RestartMission();
        }

        public void StartMission()
        {
            _flyFrom = ship.position;
            _flyT = 0f;
            _phase = Phase.Flying;
            Narrate("Rick: Hold on - we are flying closer to the binary.",
                    "Rick：坐稳，飞船正在接近双黑洞。",
                    "Rick: Hou je vast - we vliegen dichter naar het dubbele systeem.");
            SetButtons(false, false, false);
        }

        public void ContinueMission()
        {
            _phase = Phase.Waves;
            continueButton.gameObject.SetActive(false);
            simulation.Play();
            Narrate("Rick: Watch them fall toward each other. The flash marks the merger - " +
                    "the wave burst is on its way.",
                    "Rick：观察它们加速靠近。闪光代表合并，随后引力波向外传播。",
                    "Rick: Kijk hoe ze naar elkaar toe vallen. De flits markeert de samensmelting - " +
                    "de golf komt eraan.");
        }

        /// <summary>Reload the story scene: binary restored, simulation back to t = 0.</summary>
        public void RestartMission()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void Update()
        {
            switch (_phase)
            {
                case Phase.Flying:
                    _flyT += Time.deltaTime / flySeconds;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_flyT));
                    ship.position = Vector3.Lerp(_flyFrom, flyTarget, k);
                    if (binary != null)
                    {
                        Vector3 face = binary.position - ship.position;
                        face.y = 0f;
                        if (face.sqrMagnitude > 0.01f)
                            ship.rotation = Quaternion.Slerp(ship.rotation,
                                Quaternion.LookRotation(face.normalized, Vector3.up),
                                Time.deltaTime * 1.4f);
                    }
                    if (_flyT >= 1f)
                    {
                        _phase = Phase.Explaining;
                        if (cockpit != null) cockpit.OpenMission();
                        Narrate("Rick: These two black holes orbit a shared centre of mass. " +
                                "As they spiral inward their orbit speeds up - the strain they " +
                                "emit sweeps upward like a chirp. Press CONTINUE to release the simulation.",
                                "Rick：两个黑洞绕共同质心旋转。越靠近，转得越快；波的振幅与频率也会上升。点击「继续」。",
                                "Rick: Deze twee zwarte gaten draaien om een gemeenschappelijk " +
                                "zwaartepunt. Terwijl ze naar binnen spiraliseren versnelt hun baan - " +
                                "de frequentie van de golven stijgt als een 'chirp'. Druk op DOORGAAN " +
                                "om de simulatie vrij te geven.");
                        SetButtons(false, true, false);
                    }
                    break;

                case Phase.Waves:
                {
                    float s = model.WavefrontDistance(simulation.SimulationTime);
                    // Wait for the merger flash before leaving: the user watches the
                    // whole inspiral from the side, then the ship starts its ride.
                    bool merged = simulation.SimulationTime >= model.parameters.inspiralDurationSeconds;
                    if (merged && s >= SOf(ship.position) + 0.5f)
                    {
                        _phase = Phase.RidingToEarth;
                        model.GetTransverseAxes(out Vector3 u1, out _);
                        _rideOffset = u1 * rideLateralOffset;
                        Narrate("Rick: The wavefront is passing us - we are surfing it toward Earth. " +
                                "Look at the net: space itself is stretching and squeezing.",
                                "Rick：波前正穿过飞船。我们跟随被刻意放慢的可视化波形驶向地球；留意小球间的伸缩。",
                                "Rick: Het golffront passeert ons - we surfen erop richting de Aarde. " +
                                "Kijk naar het net: de ruimte zelf rekt uit en drukt samen.");
                    }
                    break;
                }

                case Phase.RidingToEarth:
                {
                    float s = model.WavefrontDistance(simulation.SimulationTime);
                    float lagged = Mathf.Max(s - rideLagBehindWavefront, 0f);
                    Vector3 target = model.Origin + model.PropagationDirection * lagged + _rideOffset;
                    target.y = Mathf.Lerp(ship.position.y, rideLiftY, Time.deltaTime * 0.8f);
                    ship.position = Vector3.Lerp(ship.position, target, Time.deltaTime * 2f);
                    // Pure lateral cruise: hold the heading perpendicular to the wave so
                    // the wavefront sweeps left-to-right across the window, and do not
                    // steer again until the Earth approach phase takes over.
                    Vector3 flat = Vector3.Cross(model.PropagationDirection, Vector3.up).normalized;
                    ship.rotation = Quaternion.Slerp(ship.rotation,
                        Quaternion.LookRotation(flat, Vector3.up), Time.deltaTime * 0.9f);

                    if (s >= _earthHitS)
                    {
                        _phase = Phase.ApproachingEarth;
                        Narrate("Rick: The wave just reached Earth - and stretched it, too. " +
                                "We are approaching for a closer look.",
                                "Rick：波刚刚抵达地球并拉伸了它。我们正在靠近观察。",
                                "Rick: De golf heeft de Aarde net bereikt - en ook uitgerekt. " +
                                "We vliegen dichterbij voor een beter kijkje.");
                    }
                    break;
                }

                case Phase.ApproachingEarth:
                {
                    if (earth == null) { _phase = Phase.Done; break; }
                    Vector3 spot = earth.position + earthWatchOffset;
                    ship.position = Vector3.Lerp(ship.position, spot, Time.deltaTime * 0.9f);
                    Vector3 look = earth.position - ship.position;
                    if (look.sqrMagnitude > 0.01f)
                        ship.rotation = Quaternion.Slerp(ship.rotation,
                            Quaternion.LookRotation(look.normalized, Vector3.up),
                            Time.deltaTime * 1.2f);
                    if ((ship.position - spot).sqrMagnitude < 0.6f)
                    {
                        _phase = Phase.Done;
                        if (cockpit != null) cockpit.OpenMission();
                        Narrate("Rick: Real gravitational waves reach Earth at a strain of only ~1e-21 - " +
                                "far too small to see. On Earth we measure them with laser " +
                                "interferometers like LIGO: two perpendicular arms stretch and squeeze, " +
                                "shifting the interference pattern. You will see that model in the " +
                                "The red L-frame by the ship is that interferometer model - " +
                                "watch its laser arms. Press RESTART MISSION to fly the whole " +
                                "sequence again.",
                                "Rick：真实引力波到达地球时应变仅约 10⁻²¹，肉眼完全无法看见。地球上的 LIGO " +
                                "用激光干涉仪测量：两条互相垂直的臂被交替拉伸压缩，从而改变干涉图样。" +
                                "飞船旁那个红色 L 形结构就是干涉仪模型，注意观察它的激光臂。" +
                                "点击「重新开始任务」可以完整重看一遍。",
                                "Rick: Echte zwaartekrachtgolven bereiken de Aarde met een amplitude van " +
                                "slechts ~1e-21 - veel te klein om te zien. Op Aarde meten we ze met " +
                                "laserinterferometers zoals LIGO: twee loodrechte armen rekken en " +
                                "drukken afwisselend uit en samen, waardoor het interferentiepatroon " +
                                "verschuift. Het rode L-frame bij het schip is dat model - " +
                                "let op de laserarmen. Druk op MISSIE HERSTARTEN om alles opnieuw te zien.");
                        SetButtons(false, false, true);
                    }
                    break;
                }
            }
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
