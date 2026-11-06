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

        /// <summary>When set, these WORLD positions replace et.vertices - the
        /// lab tour's cloned triangle is what the user answers about down
        /// there; the retired in-space model's vertices would be wrong.</summary>
        public Vector3[] stationWorldPositions;

        public bool TaskActive { get; private set; }
        int _correctStation = -1;

        /// <summary>Fire a fresh wave at the stations and start a prediction
        /// round. The front KEEPS travelling - the learner watches the ring
        /// close in and answers before it strikes; a frozen wave taught
        /// nothing about arrival order.</summary>
        public void BeginPrediction()
        {
            if (et == null || model == null || simulation == null) return;
            if (TaskActive) return;
            // Same gate as replay/reset: mid-transit the wave clock belongs to
            // the mission choreography - a rewind would freeze the journey.
            if (ui != null && ui.story != null && !ui.story.CanPilot) return;

            // Bring the console drawer onto the ET page FIRST - the answer
            // keys (D1/D2/D3) and this button all live there, so the task is
            // never announced while the user is staring at the WAVE LAB tab.
            var cockpit = ui != null && ui.story != null ? ui.story.cockpit : null;
            if (cockpit != null) cockpit.OpenControls();
            var tabs = ui != null ? ui.GetComponent<ConsoleTabs>() : null;
            if (tabs != null) tabs.ShowET();

            // Rewind the clock so the front is ~5 sim-seconds short of the
            // NEAREST station, then let it RUN: enough inbound time to see
            // the ring approach, and it always arrives at exactly one station
            // first - no more "let the wave travel" dead ends (down in the
            // lab the mission wave has long passed every station).
            float v = model.parameters != null
                ? Mathf.Max(model.parameters.visualizationSpeedMps, 1e-4f) : 1f;
            float minDist = float.MaxValue;
            for (int i = 0; i < 3; i++)
                minDist = Mathf.Min(minDist,
                    model.PropagationDistance(StationWorldPos(i)));
            if (minDist == float.MaxValue) minDist = 0f;
            float t0 = model.emissionDelay + minDist / v - 5f;
            simulation.RestartAt(Mathf.Max(0f, t0), true);

            _correctStation = FindNextStation();
            if (_correctStation < 0)
            {
                Announce(ExperienceSettings.IsChinese
                    ? "波前尚未抵达任何一个测站或已全部通过 - 先让波传播一段再试。"
                    : "No station is still waiting on the wave - let it travel first.");
                return;
            }

            TaskActive = true;
            UiSpotlightTarget.SetGroup("station-", true);
            ResearchLogger.Instance?.LogEvent("PredictionTask", "begin");
            Announce(ExperienceSettings.IsChinese
                ? "一道新的引力波正在奔向三台测站——预测哪一站先被波前点亮？趁波还没到，按 D1 / D2 / D3 作答。"
                : ExperienceSettings.IsDutch
                    ? "Een nieuwe golf nadert de drie stations - voorspel welk station eerst oplicht: kies D1 / D2 / D3 vóór de aankomst."
                    : "A fresh wavefront is racing toward the triangle - predict which station lights up first: answer D1 / D2 / D3 before it arrives.");
        }

        /// <summary>Watches the inbound front: when it reaches the nearest
        /// station unanswered, the outcome reveals itself - that corner's
        /// detector lighting up IS the answer.</summary>
        void Update()
        {
            if (!TaskActive || _correctStation < 0) return;
            float tau = model.RetardedTime(
                StationWorldPos(_correctStation), simulation.SimulationTime);
            if (tau >= 0f) ResolveArrival();
        }

        void ResolveArrival()
        {
            int hit = _correctStation;
            TaskActive = false;
            _correctStation = -1;
            UiSpotlightTarget.SetGroup("station-", false);
            if (et != null) et.SelectDetector(hit);
            ResearchLogger.Instance?.LogEvent("PredictionTask",
                $"unanswered=D{hit + 1}");
            Announce(ExperienceSettings.IsChinese
                ? $"波前已经到达——D{hit + 1} 最先被点亮，这就是答案。"
                : ExperienceSettings.IsDutch
                    ? $"Het front arriveerde - D{hit + 1} lichtte als eerste op. Dat was het antwoord."
                    : $"The front arrived - D{hit + 1} lit up first. That was the answer.");
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
            if (stationWorldPositions != null && i < stationWorldPositions.Length)
                return stationWorldPositions[i];
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
                UiSpotlightTarget.SetGroup("station-", false);
                if (et != null) et.SelectDetector(index);
                Announce(ExperienceSettings.IsChinese
                    ? $"正确——D{index + 1} 离波前最近，先测到信号。看它的探测器亮起来。"
                    : $"Correct - D{index + 1} is nearest the front and registers it first. Watch its detector light up.");
            }
            else
            {
                Announce(ExperienceSettings.IsChinese
                    ? "不对——再看此刻波环离哪一站最近？趁波还没到，再答一次。"
                    : "Not quite - look which station the ring is closest to now. Answer again before it arrives.");
            }
        }

        void Announce(string message)
        {
            if (subtitles != null) { subtitles.ShowAnswer(message); return; }
            if (ui != null && ui.statusText != null) ui.statusText.text = message;
        }
    }
}
