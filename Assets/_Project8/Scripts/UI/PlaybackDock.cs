using UnityEngine;
using UnityEngine.UI;
using Project8.Core;
using Project8.Story;

namespace Project8.UI
{
    /// <summary>
    /// The persistent Play/Pause dock: a single always-enabled canvas outside
    /// every drawer (the SimulationUI lives inside the collapsible Controls
    /// drawer, so it cannot own this label). The button delegates to
    /// StoryDirector.TogglePlayback so it shares the mission's exact semantics -
    /// Play at the parked start begins the mission, afterwards it pauses and
    /// resumes the same simulation clock.
    /// </summary>
    public class PlaybackDock : MonoBehaviour
    {
        public SimulationController simulation;
        public StoryDirector story;
        public Text label;
        public Text timeText;

        void Update()
        {
            if (simulation == null) return;
            bool playing = simulation.IsPlaying;
            // The dock shows START only in the actual parked Waiting state -
            // never during a wave preview sitting at the same phase.
            bool dockStart = story != null && story.MissionWaiting && !story.InPreview;
            if (label != null)
                label.text = dockStart
                    ? (ExperienceSettings.IsChinese ? "开始" :
                       ExperienceSettings.IsDutch ? "START" : "START")
                    : playing
                        ? (ExperienceSettings.IsChinese ? "暂停" :
                           ExperienceSettings.IsDutch ? "PAUZE" : "PAUSE")
                        : (ExperienceSettings.IsChinese ? "播放" :
                           ExperienceSettings.IsDutch ? "AFSPELEN" : "PLAY");
            if (timeText != null)
                timeText.text = $"t = {simulation.SimulationTime:F1} s";
        }

        /// <summary>Button handler: one transport action, mission-aware.</summary>
        public void OnPlayPause()
        {
            if (story != null) story.TogglePlayback();
            else if (simulation != null) simulation.TogglePlayPause();
        }
    }
}
