using UnityEngine;
using UnityEngine.Events;

namespace Project8.Core
{
    /// <summary>
    /// Drives simulation time for the Digital Twin. Supports play, pause, slow-motion,
    /// replay and reset - the control loop described in the project brief.
    /// UI widgets bind to the public methods; other components read SimulationTime.
    /// </summary>
    public class SimulationController : MonoBehaviour
    {
        public SimulationParameters parameters;

        public static readonly float[] SpeedPresets = { 0.25f, 0.5f, 1f };

        public bool IsPlaying { get; private set; } = true;
        public float SimulationTime { get; private set; }
        public int SpeedIndex { get; private set; } = 2;
        public float TimeScale => SpeedPresets[Mathf.Clamp(SpeedIndex, 0, SpeedPresets.Length - 1)];

        [Header("Events")]
        public UnityEvent<bool> onPlayingChanged = new UnityEvent<bool>();
        public UnityEvent<int> onSpeedIndexChanged = new UnityEvent<int>();
        public UnityEvent onSimulationReset = new UnityEvent();

        void Update()
        {
            if (IsPlaying) SimulationTime += Time.deltaTime * TimeScale;
        }

        public void Play() => SetPlaying(true);
        public void Pause() => SetPlaying(false);
        public void TogglePlayPause() => SetPlaying(!IsPlaying);

        void SetPlaying(bool value)
        {
            if (IsPlaying == value) return;
            IsPlaying = value;
            onPlayingChanged.Invoke(value);
        }

        public void SetSpeedIndex(int index)
        {
            index = Mathf.Clamp(index, 0, SpeedPresets.Length - 1);
            if (SpeedIndex == index) return;
            SpeedIndex = index;
            onSpeedIndexChanged.Invoke(index);
        }

        // Parameterless wrappers so editor persistent UnityEvents can bind to fixed speeds.
        public void SetSpeedQuarter() => SetSpeedIndex(0);
        public void SetSpeedHalf() => SetSpeedIndex(1);
        public void SetSpeedNormal() => SetSpeedIndex(2);

        /// <summary>Restart the wave from t = 0 and play it again.</summary>
        public void Replay()
        {
            SimulationTime = 0f;
            onSimulationReset.Invoke();
            Play();
        }

        /// <summary>Reset to t = 0 and pause, ready for inspection.</summary>
        public void ResetSimulation()
        {
            SimulationTime = 0f;
            onSimulationReset.Invoke();
            Pause();
        }
    }
}
