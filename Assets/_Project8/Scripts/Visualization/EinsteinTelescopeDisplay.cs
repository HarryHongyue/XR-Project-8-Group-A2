using UnityEngine;
using UnityEngine.UI;
using Project8.Core;
using Project8.UI;

namespace Project8.Visualization
{
    /// <summary>
    /// The Einstein Telescope observatory display: an equilateral triangle of three
    /// corner stations (60-degree arms each), every station hosting a PAIR of nested
    /// interferometer lanes - a low-frequency (LF) and a high-frequency (HF) instrument
    /// like the published 2020 design baseline. This is a teaching model: the two
    /// lanes share the same qualitative strain response; their real noise-split and
    /// the full power/signal-recycling optics are NOT numerically simulated.
    ///
    /// Selection model: one station is highlighted at a time. SelectDetector()
    /// reveals that station's optical paths (beams, mirrors, markers, trace) while
    /// the other stations' nested lanes stay dimmed out; SetDetailVisible(false)
    /// collapses even the selected detail. Channel values live in the console
    /// inspector text - there are no world-space prose labels.
    /// </summary>
    public class EinsteinTelescopeDisplay : MonoBehaviour
    {
        [Header("Triangle")]
        [Tooltip("The three local corner stations (metres, equilateral, y=0 plane).")]
        public Vector3[] vertices =
        {
            new Vector3(-6f, 0f, -3.4641016f),
            new Vector3(6f, 0f, -3.4641016f),
            new Vector3(0f, 0f, 6.9282032f),
        };

        [Header("Optics - six lanes, two per station (LF then HF)")]
        public LaserInterferometer[] detectors;
        [Tooltip("Per-station parent of the detailed optics; shown only when selected.")]
        public Transform[] detailRoots;

        [Header("Selection")]
        [Range(0, 2)] public int SelectedDetector;
        [Tooltip("Master reveal toggle from the console inspector.")]
        public bool DetailVisible = true;

        [Header("Console channel readout + selection feedback")]
        public Text inspectorText;
        [Tooltip("One-line 'Station N - detail ON/OFF' state indicator.")]
        public Text statusText;
        [Tooltip("D1/D2/D3 selector buttons - the selected one is tinted active.")]
        public Button[] stationButtons;

        public GravitationalWaveModel model;
        public SimulationController simulation;

        float _nextRefresh;
        static readonly Color LaneDim = new Color(0.38f, 0.40f, 0.48f, 0.45f);
        static readonly Color LaneLit = Color.white;

        void Start() => ApplyVisibility();

        /// <summary>Pick station 0..2 (D1/D2/D3) - reveals only its optical lanes.</summary>
        public void SelectDetector(int index)
        {
            SelectedDetector = Mathf.Clamp(index, 0, 2);
            ApplyVisibility();
            RefreshInspector();
        }

        /// <summary>Show or collapse the detailed optics of the selected station.</summary>
        public void SetDetailVisible(bool visible)
        {
            DetailVisible = visible;
            ApplyVisibility();
            RefreshInspector();
        }

        /// <summary>UI toggle entry point for the DETAIL button.</summary>
        public void ToggleDetail() => SetDetailVisible(!DetailVisible);

        void ApplyVisibility()
        {
            for (int s = 0; s < 3; s++)
            {
                bool sel = s == SelectedDetector;
                // The detailed optics belong to the selected station only.
                if (detailRoots != null && s < detailRoots.Length &&
                    detailRoots[s] != null)
                    detailRoots[s].gameObject.SetActive(DetailVisible && sel);
                // The arm lanes stay drawn for the whole triangle - dimmed when
                // not selected so the baseline geometry stays readable.
                Color c = sel ? LaneLit : LaneDim;
                for (int lane = 0; lane < 2; lane++)
                {
                    var d = LaneFor(s, lane);
                    if (d == null) continue;
                    Tint(d.outboundA, c); Tint(d.inboundA, c);
                    Tint(d.outboundB, c); Tint(d.inboundB, c);
                }
            }
            // Selector button feedback: the chosen station is lit, others dim.
            if (stationButtons != null)
                for (int i = 0; i < stationButtons.Length; i++)
                    if (stationButtons[i] != null &&
                        stationButtons[i].targetGraphic != null)
                        stationButtons[i].targetGraphic.color =
                            i == SelectedDetector ? LaneLit
                                : new Color(.7f, .74f, .85f, .55f);
            if (statusText != null)
            {
                if (ExperienceSettings.IsChinese)
                    LocalizedText.UseChineseFont(statusText);
                string station = ExperienceSettings.IsChinese ? "测站" : "Station";
                string detail = DetailVisible
                    ? (ExperienceSettings.IsChinese ? "细节 开"
                       : ExperienceSettings.IsDutch ? "detail AAN" : "detail ON")
                    : (ExperienceSettings.IsChinese ? "细节 关"
                       : ExperienceSettings.IsDutch ? "detail UIT" : "detail OFF");
                statusText.text = $"{station} {SelectedDetector + 1}  ·  {detail}";
            }
        }

        static void Tint(LineRenderer lr, Color c)
        {
            if (lr == null) return;
            lr.startColor = c;
            lr.endColor = c;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            RefreshInspector();
        }

        /// <summary>Two-line channel readout for the selected station: LF and HF
        /// phase + dark-port signal. Explicit that the pair is illustrative.</summary>
        public void RefreshInspector()
        {
            if (inspectorText == null || detectors == null) return;
            int s = Mathf.Clamp(SelectedDetector, 0, 2);
            var lf = LaneFor(s, 0);
            var hf = LaneFor(s, 1);
            string phase = ExperienceSettings.IsChinese ? "相位"
                : ExperienceSettings.IsDutch ? "fase" : "phase";
            string signal = ExperienceSettings.IsChinese ? "信号"
                : ExperienceSettings.IsDutch ? "signaal" : "signal";
            if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(inspectorText);
            inspectorText.text =
                $"D{s + 1}  LF   {phase} {Phase(lf)}   {signal} {Signal(lf)}\n" +
                $"D{s + 1}  HF   {phase} {Phase(hf)}   {signal} {Signal(hf)}";
        }

        LaserInterferometer LaneFor(int station, int lane)
        {
            int i = station * 2 + lane;
            return detectors != null && i < detectors.Length ? detectors[i] : null;
        }

        static string Phase(LaserInterferometer d) =>
            d == null ? "--" : d.CurrentPhase.ToString("0.00") + " rad";
        static string Signal(LaserInterferometer d) =>
            d == null ? "--" : d.DarkPortIntensity.ToString("0.00");
    }
}
