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
                amplitudeValueText.text = $"h0 = {parameters.strainAmplitude:F2} (exaggerated)";
            if (frequencyValueText != null)
                frequencyValueText.text = $"f = {parameters.frequencyHz:F2} Hz";
            if (exaggerationSlider != null)
                exaggerationSlider.value = parameters.displacementExaggeration;
            if (exaggerationValueText != null)
                exaggerationValueText.text =
                    parameters.displacementExaggeration <= 0.01f
                        ? "real scale (invisible)"
                        : $"x{parameters.displacementExaggeration:F2} exaggerated";
            _refreshing = false;
        }

        void Update()
        {
            if (playPauseButtonText != null)
                playPauseButtonText.text = simulation.IsPlaying ? "Pause" : "Play";
            if (perspectiveButtonText != null)
                perspectiveButtonText.text = perspective.Mode == PerspectiveMode.Observer
                    ? "Ride with the wave" : "Return to observer";
            if (statusText != null)
                statusText.text =
                    $"t = {simulation.SimulationTime:F1} s   |   " +
                    $"{(simulation.IsPlaying ? "playing" : "paused")} at {simulation.TimeScale:F2}x   |   " +
                    $"view: {(perspective.Mode == PerspectiveMode.Observer ? "observer" : "riding the wave (visualization frame)")}";
        }
    }
}
