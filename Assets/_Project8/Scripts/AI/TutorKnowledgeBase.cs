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

        [TextArea(2, 6)]
        public string fallbackAnswerZh =
            "好问题。在这个可视化里，简化的引力波从黑洞并合处向外扩散，" +
            "传向爱因斯坦望远镜的三个测站。你可以问：应变（h = ΔL/L）、拉伸与挤压、" +
            "波前圆环、为什么演示把波放慢了、或者哪些数值被夸大了。";

        [TextArea(2, 6)]
        public string fallbackAnswerNl =
            "Goede vraag. In deze visualisatie plant een vereenvoudigde zwaartekrachtgolf " +
            "zich voort vanuit een samensmeltend paar zwarte gaten naar de drie stations " +
            "van de Einstein Telescope. Vraag bijvoorbeeld naar rek (h = dL/L), uitrekken " +
            "en samendrukken, de golfrand, waarom de beweging vertraagd is, of welke " +
            "waarden overdreven zijn.";

        /// <summary>No-match reply in the selected UI language - the remote-failure
        /// path used to drop to English no matter what the user picked.</summary>
        string LocalizedFallback()
        {
            if (Project8.UI.ExperienceSettings.IsChinese) return fallbackAnswerZh;
            if (Project8.UI.ExperienceSettings.IsDutch) return fallbackAnswerNl;
            return fallbackAnswer;
        }

        /// <summary>Returns the best-scoring answer for a free-text question.</summary>
        public string FindBest(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return LocalizedFallback();
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
            return best != null ? best.answer : LocalizedFallback();
        }
    }
}
