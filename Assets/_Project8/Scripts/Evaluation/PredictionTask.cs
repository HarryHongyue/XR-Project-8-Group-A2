using UnityEngine;
using Project8.Core;
using Project8.Visualization;
using Project8.UI;

namespace Project8.Evaluation
{
    /// <summary>
    /// Guided prediction task from the brief's evaluation section. While the wave
    /// is in flight the user pauses and predicts WHICH EINSTEIN TELESCOPE STATION
    /// (D1/D2/D3) the expanding front reaches next - the stations sit at different
    /// distances from the merger, so the retarded-time ordering is a real physics
    /// question, not a trick. Answers are picked with the D1/D2/D3 console buttons
    /// and logged for the research component.
    /// </summary>
    public class PredictionTask : MonoBehaviour
    {
        public EinsteinTelescopeDisplay et;
        public GravitationalWaveModel model;
        public SimulationController simulation;
        public SimulationUI ui;
        public RickSubtitles subtitles;

        public bool TaskActive { get; private set; }
        int _correctStation = -1;

        /// <summary>Pause the wave and start a prediction round.</summary>
        public void BeginPrediction()
        {
            if (et == null || model == null || simulation == null) return;
            if (TaskActive) return;

            _correctStation = FindNextStation();
            if (_correctStation < 0)
            {
                Announce(ExperienceSettings.IsChinese
                    ? "波前尚未抵达任何一个测站或已全部通过 - 先让波传播一段再试。"
                    : "No station is still waiting on the wave - let it travel first.");
                return;
            }

            simulation.Pause();
            TaskActive = true;
            ResearchLogger.Instance?.LogEvent("PredictionTask", "begin");
            Announce(ExperienceSettings.IsChinese
                ? "预测：引力波下一步会先到达 ET 的哪个测站？按 D1 / D2 / D3 作答。"
                : ExperienceSettings.IsDutch
                    ? "Voorspel: welk ET-station bereikt de golf eerst? Kies D1 / D2 / D3."
                    : "PREDICT: which ET station does the wavefront reach next? Answer with D1 / D2 / D3.");
        }

        /// <summary>Index of the station with the smallest positive time-to-hit,
        /// or -1 when the front has already passed every station.</summary>
        int FindNextStation()
        {
            float t = simulation.SimulationTime;
            int best = -1;
            float bestTau = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                Vector3 pos = StationWorldPos(i);
                float tau = model.RetardedTime(pos, t);
                float timeToHit = -tau; // how long until the front reaches this station
                if (timeToHit > 0.05f && timeToHit < bestTau) { bestTau = timeToHit; best = i; }
            }
            return best;
        }

        Vector3 StationWorldPos(int i)
        {
            if (et.transform != null && et.vertices != null && i < et.vertices.Length)
                return et.transform.TransformPoint(et.vertices[i]);
            return et.transform.position;
        }

        /// <summary>Called by the D1/D2/D3 console buttons - during an active
        /// prediction they answer the task; otherwise the display selection is
        /// the only effect.</summary>
        public void AnswerStation(int index)
        {
            if (!TaskActive || index < 0 || index > 2) return;
            bool correct = index == _correctStation;
            ResearchLogger.Instance?.LogEvent("PredictionTask",
                $"answered=D{index + 1} {(correct ? "correct" : "wrong")}");
            if (correct)
            {
                TaskActive = false;
                _correctStation = -1;
                Announce(ExperienceSettings.IsChinese
                    ? $"正确 - D{index + 1} 离波前最近，会先测到信号。按播放继续观察它抵达。"
                    : $"Correct - D{index + 1} is closest to the wavefront and registers it first. Press Play to watch it arrive.");
            }
            else
            {
                Announce(ExperienceSettings.IsChinese
                    ? "不对 - 想一想波前已经扩散到多远，哪一站离波前最近？再试一次。"
                    : "Not quite - think about how far the ring has already spread. Which station is closest to it? Try again.");
            }
        }

        void Announce(string message)
        {
            if (subtitles != null) { subtitles.ShowAnswer(message); return; }
            if (ui != null && ui.statusText != null) ui.statusText.text = message;
        }
    }
}
