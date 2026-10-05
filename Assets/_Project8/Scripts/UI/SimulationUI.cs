using UnityEngine;
using UnityEngine.UI;
using Project8.Core;
using Project8.Perspective;

namespace Project8.UI
{
    /// <summary>
    /// Binds the world-space control panel to the simulation: play/pause, slow-motion,
    /// replay, reset, perspective toggle and the two main model parameters
    /// (strain amplitude h0 and frequency f). Also owns the status line that reports
    /// simulation time, playback state, speed preset and perspective mode.
    /// </summary>
    public class SimulationUI : MonoBehaviour
    {
        [Header("References")]
        public SimulationController simulation;
        public GravitationalWaveModel model;
        public SimulationParameters parameters;
        public PerspectiveController perspective;

        [Header("Widgets (assigned by the scene builder)")]
        public Text statusText;
        public Text amplitudeValueText;
        public Text frequencyValueText;
        public Text playPauseButtonText;
        public Text perspectiveButtonText;
        public Slider amplitudeSlider;
        public Slider frequencySlider;
        public Slider exaggerationSlider;
        public Text exaggerationValueText;

        bool _refreshing; // guard against slider feedback loops

        void Start()
        {
            if (parameters != null) parameters.Changed += RefreshWidgets;
            RefreshWidgets();
        }

        void OnDestroy()
        {
            if (parameters != null) parameters.Changed -= RefreshWidgets;
        }

        // --- Button handlers -------------------------------------------------
        public void OnPlayPause() => simulation.TogglePlayPause();
        public void OnReplay() => simulation.Replay();
        public void OnReset() => simulation.ResetSimulation();
        public void OnSpeedQuarter() => simulation.SetSpeedQuarter();
        public void OnSpeedNormal() => simulation.SetSpeedNormal();
        public void OnTogglePerspective() => perspective.TogglePerspective();

        // --- Slider handlers (bound dynamically by the scene builder) --------
        public void SetAmplitude(float v)
        {
            if (_refreshing) return;
            parameters.strainAmplitude = v;
            parameters.NotifyChanged();
            Evaluation.ResearchLogger.Instance?.LogEvent("Parameter", $"strainAmplitude={v:F2}");
        }

        public void SetFrequency(float v)
        {
            if (_refreshing) return;
            parameters.frequencyHz = v;
            parameters.NotifyChanged();
            Evaluation.ResearchLogger.Instance?.LogEvent("Parameter", $"frequencyHz={v:F2}");
        }

        public void SetExaggeration(float v)
        {
            if (_refreshing) return;
            parameters.displacementExaggeration = v;
            parameters.NotifyChanged();
            Evaluation.ResearchLogger.Instance?.LogEvent("Parameter", $"exaggeration={v:F2}");
        }

        // --- Widget refresh ----------------------------------------------------
        public void RefreshWidgets()
        {
            _refreshing = true;
            if (amplitudeSlider != null) amplitudeSlider.value = parameters.strainAmplitude;
            if (frequencySlider != null) frequencySlider.value = parameters.frequencyHz;
            if (amplitudeValueText != null)
            {
                if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(amplitudeValueText);
                amplitudeValueText.text = ExperienceSettings.IsChinese
                    ? $"h0 = {parameters.strainAmplitude:F2}（夸张显示）"
                    : ExperienceSettings.IsDutch
                        ? $"h0 = {parameters.strainAmplitude:F2} (overdreven)"
                        : $"h0 = {parameters.strainAmplitude:F2} (exaggerated)";
            }
            if (frequencyValueText != null)
                frequencyValueText.text = $"f = {parameters.frequencyHz:F2} Hz";
            if (exaggerationSlider != null)
                exaggerationSlider.value = parameters.displacementExaggeration;
            if (exaggerationValueText != null)
            {
                if (ExperienceSettings.IsChinese) LocalizedText.UseChineseFont(exaggerationValueText);
                exaggerationValueText.text = ExperienceSettings.IsChinese
                    ? parameters.displacementExaggeration <= 0.01f
                        ? "实际尺度（不可见）" : $"x{parameters.displacementExaggeration:F2} 夸张"
                    : ExperienceSettings.IsDutch
                        ? parameters.displacementExaggeration <= 0.01f
                            ? "echte schaal (onzichtbaar)" : $"x{parameters.displacementExaggeration:F2} overdreven"
                        : parameters.displacementExaggeration <= 0.01f
                            ? "real scale (invisible)" : $"x{parameters.displacementExaggeration:F2} exaggerated";
            }
            _refreshing = false;
        }

        void Update()
        {
            bool chinese = ExperienceSettings.IsChinese;
            bool dutch = ExperienceSettings.IsDutch;
            if (playPauseButtonText != null)
                playPauseButtonText.text = simulation.IsPlaying
                    ? (chinese ? "暂停" : dutch ? "Pauze" : "Pause")
                    : (chinese ? "播放" : dutch ? "Afspelen" : "Play");
            if (perspectiveButtonText != null)
                perspectiveButtonText.text = perspective.Mode == PerspectiveMode.Observer
                    ? (chinese ? "跟随引力波" : dutch ? "Surfen op de golf" : "Ride with the wave")
                    : (chinese ? "返回观察位置" : dutch ? "Terug naar waarnemer" : "Return to observer");
            if (statusText != null)
            {
                if (chinese) LocalizedText.UseChineseFont(statusText);
                statusText.text = chinese
                    ? $"时间 {simulation.SimulationTime:F1} 秒  |  {(simulation.IsPlaying ? "播放中" : "已暂停")} " +
                      $"{simulation.TimeScale:F2} 倍速  |  {(perspective.Mode == PerspectiveMode.Observer ? "观察" : "跟随波前")}"
                    : dutch
                        ? $"t = {simulation.SimulationTime:F1} s   |   " +
                          $"{(simulation.IsPlaying ? "speelt" : "gepauzeerd")} op {simulation.TimeScale:F2}x   |   " +
                          $"zicht: {(perspective.Mode == PerspectiveMode.Observer ? "waarnemer" : "surft op de golf")}"
                        : $"t = {simulation.SimulationTime:F1} s   |   " +
                          $"{(simulation.IsPlaying ? "playing" : "paused")} at {simulation.TimeScale:F2}x   |   " +
                          $"view: {(perspective.Mode == PerspectiveMode.Observer ? "observer" : "riding the wave (visualization frame)")}";
            }
        }
    }
}
