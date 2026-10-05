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
    ///   6. wave reaches Earth - exaggerated deformation, then "ENTER WAVE LAB"
    /// </summary>
    public class StoryDirector : MonoBehaviour
    {
        public SimulationController simulation;
        public GravitationalWaveModel model;
        public Transform ship;
        public Transform earth;
        public CockpitControls cockpit;

        [Header("UI wiring")]
        public Text narrationText;
        public Button startButton;
        public Button continueButton;
        public Button labButton;

        [Header("Choreography")]
        public Vector3 flyTarget = new Vector3(0f, 0.9f, 7f);
        public float flySeconds = 7f;
        public float rideLagBehindWavefront = 1.5f;
        public float rideLateralOffset = 3f;
        public float rideLiftY = 3.2f;
        public string waveLabScene = "WaveLab";

        enum Phase { Waiting, Flying, Explaining, Waves, RidingToEarth, Done }
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
                    "Rick：欢迎登上飞船。前方的双黑洞即将合并，碰撞会释放引力波。准备好后点击「开始任务」。");
            SetButtons(true, false, false);
        }

        float SOf(Vector3 p) => Vector3.Dot(p - model.Origin, model.PropagationDirection);

        public void Advance()
        {
            if (_phase == Phase.Waiting) StartMission();
            else if (_phase == Phase.Explaining) ContinueMission();
            else if (_phase == Phase.Done) GoToWaveLab();
        }

        public void StartMission()
        {
            _flyFrom = ship.position;
            _flyT = 0f;
            _phase = Phase.Flying;
            Narrate("Rick: Hold on - we are flying closer to the binary.",
                    "Rick：坐稳，飞船正在接近双黑洞。");
            SetButtons(false, false, false);
        }

        public void ContinueMission()
        {
            _phase = Phase.Waves;
            continueButton.gameObject.SetActive(false);
            simulation.Play();
            Narrate("Rick: Watch them fall toward each other. The flash marks the merger - " +
                    "the wave burst is on its way.",
                    "Rick：观察它们加速靠近。闪光代表合并，随后引力波向外传播。");
        }

        public void GoToWaveLab()
        {
            SceneManager.LoadScene(waveLabScene);
        }

        void Update()
        {
            switch (_phase)
            {
                case Phase.Flying:
                    _flyT += Time.deltaTime / flySeconds;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_flyT));
                    ship.position = Vector3.Lerp(_flyFrom, flyTarget, k);
                    if (_flyT >= 1f)
                    {
                        _phase = Phase.Explaining;
                        if (cockpit != null) cockpit.OpenMission();
                        Narrate("Rick: These two black holes orbit a shared centre of mass. " +
                                "As they spiral inward their orbit speeds up - the strain they " +
                                "emit sweeps upward like a chirp. Press CONTINUE to release the simulation.",
                                "Rick：两个黑洞绕共同质心旋转。越靠近，转得越快；波的振幅与频率也会上升。点击「继续」。");
                        SetButtons(false, true, false);
                    }
                    break;

                case Phase.Waves:
                {
                    float s = model.WavefrontDistance(simulation.SimulationTime);
                    if (s >= SOf(ship.position) + 0.5f)
                    {
                        _phase = Phase.RidingToEarth;
                        model.GetTransverseAxes(out Vector3 u1, out _);
                        _rideOffset = u1 * rideLateralOffset;
                        Narrate("Rick: The wavefront is passing us - we are surfing it toward Earth. " +
                                "Look at the net: space itself is stretching and squeezing.",
                                "Rick：波前正穿过飞船。我们跟随被刻意放慢的可视化波形驶向地球；留意小球间的伸缩。");
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

                    if (s >= _earthHitS)
                    {
                        _phase = Phase.Done;
                        if (cockpit != null) cockpit.OpenMission();
                        Narrate("Rick: The wave just reached Earth - and stretched it, too. " +
                                "Real GWs do this at strain ~1e-21: invisible, so we exaggerated it. " +
                                "Now it is your turn - enter the WAVE LAB.",
                                "Rick：波到达地球。真实应变约为 10⁻²¹，肉眼无法看见，这里有意夸张。接下来进入波形实验室。");
                        SetButtons(false, false, true);
                    }
                    break;
                }
            }
        }

        void Narrate(string english, string chinese)
        {
            if (narrationText == null) return;
            if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(narrationText);
            narrationText.text = ExperienceSettings.IsChinese ? chinese : english;
        }

        void SetButtons(bool start, bool cont, bool lab)
        {
            if (startButton != null) startButton.gameObject.SetActive(start);
            if (continueButton != null) continueButton.gameObject.SetActive(cont);
            if (labButton != null) labButton.gameObject.SetActive(lab);
        }
    }
}
