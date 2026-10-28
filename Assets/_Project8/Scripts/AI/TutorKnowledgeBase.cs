using System.Collections.Generic;
using UnityEngine;

namespace Project8.AI
{
    /// <summary>
    /// Curated knowledge base for the AI tutor. Every entry is grounded in the project
    /// Starter Pack: propagation, strain h = dL/L, transverse stretch/squeeze, the ring
    /// of test particles, and the explicit visualization simplifications (slowed speed,
    /// exaggerated deformation). Local lookup is the offline backend for AITutor.
    /// </summary>
    [CreateAssetMenu(fileName = "TutorKnowledgeBase", menuName = "Project 8/Tutor Knowledge Base")]
    public class TutorKnowledgeBase : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            [Tooltip("Lowercase keywords; a hit on any of them counts toward this answer.")]
            public string[] keywords;
            [TextArea(2, 6)] public string answer;
        }

        public List<Entry> entries = new List<Entry>();

        [TextArea(2, 6)]
        public string fallbackAnswer =
            "Good question. In this visualization a simplified gravitational wave expands " +
            "outward from a black-hole merger toward the Einstein Telescope's three " +
            "stations. Try asking about strain, stretch and squeeze, the wavefront ring, " +
            "why the motion is slowed down, or which values are exaggerated.";

        /// <summary>Returns the best-scoring answer for a free-text question.</summary>
        public string FindBest(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return fallbackAnswer;
            string q = question.ToLowerInvariant();
            Entry best = null;
            int bestScore = 0;
            foreach (var e in entries)
            {
                int score = 0;
                foreach (var kw in e.keywords)
                    if (!string.IsNullOrWhiteSpace(kw) && q.Contains(kw.Trim().ToLowerInvariant()))
                        score++;
                if (score > bestScore) { bestScore = score; best = e; }
            }
            return best != null ? best.answer : fallbackAnswer;
        }
    }
}
