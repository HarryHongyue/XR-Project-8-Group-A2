using UnityEngine;
using Project8.Core;
using Project8.Visualization;

namespace Project8.Evaluation
{
    /// <summary>
    /// Guided prediction task from the brief's evaluation section: the simulation pauses
    /// while the wave is in flight and asks "which test mass will be affected next?".
    /// The user selects a mass (XR ray poke or editor mouse click); the answer is scored
    /// against retarded-time distances and logged for the research component.
    /// </summary>
    public class PredictionTask : MonoBehaviour
    {
        public TestMassField field;
        public GravitationalWaveModel model;
        public SimulationController simulation;
        public UI.SimulationUI ui;

        public bool TaskActive { get; private set; }
        TestMass _correct;
        float _resumeTime;

        /// <summary>Pause the wave and start a prediction round.</summary>
        public void BeginPrediction()
        {
            if (field == null || model == null || simulation == null) return;
            if (TaskActive) return;

            _correct = FindNextMass();
            if (_correct == null) return; // wave has already passed everything

            _resumeTime = simulation.SimulationTime;
            simulation.Pause();
            TaskActive = true;
            ResearchLogger.Instance?.LogEvent("PredictionTask", "begin");
            if (ui != null && ui.statusText != null)
                ui.statusText.text = "PREDICT: which test mass will move next? Select it.";
        }

        TestMass FindNextMass()
        {
            float t = simulation.SimulationTime;
            TestMass best = null;
            float bestTau = float.MaxValue;
            foreach (var m in field.Masses)
            {
                Vector3 worldRest = field.transform.TransformPoint(m.RestLocalPosition);
                float tau = model.RetardedTime(worldRest, t);
                float timeToHit = -tau; // how long until the front reaches this mass
                if (timeToHit > 0f && timeToHit < bestTau) { bestTau = timeToHit; best = m; }
            }
            return best;
        }

        /// <summary>Called by TestMass when the user selects a candidate.</summary>
        public void TrySelect(TestMass mass)
        {
            if (!TaskActive || mass == null) return;
            bool correct = mass == _correct;
            mass.SetHighlighted(correct);
            ResearchLogger.Instance?.LogEvent("PredictionTask",
                $"answered={(correct ? "correct" : "wrong")}");
            if (ui != null && ui.statusText != null)
                ui.statusText.text = correct
                    ? "Correct - this mass is closest to the approaching wavefront. Press Play."
                    : "Not quite - think about where the wavefront has reached. Try again.";
            if (correct) TaskActive = false;
        }
    }
}
