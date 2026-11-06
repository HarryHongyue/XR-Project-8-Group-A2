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
        public RickSubtitles subtitles;
        public Text label;

        void Update()
        {
            if (simulation == null) return;
            if (subtitles == null)
                subtitles = FindAnyObjectByType<RickSubtitles>();
            // The button is the MASTER pause: "PAUSE" while the sim clock OR
            // the narration is live, "PLAY" once the transport froze either.
            bool narrPaused = subtitles != null && subtitles.Paused;
            bool narrLive = subtitles != null && subtitles.Busy && !narrPaused;
            bool live = simulation.IsPlaying || narrLive;
            // START appears ONLY once the parked intro has fully played and the
            // mission can actually begin - while the welcome line is live or
            // paused the button is a transport control, not a start button.
            bool dockStart = story != null && story.MissionWaiting &&
                !story.InPreview && !narrLive && !narrPaused;
            if (label != null)
                label.text = dockStart
                    ? (ExperienceSettings.IsChinese ? "开始" :
                       ExperienceSettings.IsDutch ? "START" : "START")
                    : live
                        ? (ExperienceSettings.IsChinese ? "暂停" :
                           ExperienceSettings.IsDutch ? "PAUZE" : "PAUSE")
                        : (ExperienceSettings.IsChinese ? "播放" :
                           ExperienceSettings.IsDutch ? "AFSPELEN" : "PLAY");
        }

        /// <summary>Button handler: one transport action, mission-aware.</summary>
        public void OnPlayPause()
        {
            if (story != null) story.TogglePlayback();
            else if (simulation != null) simulation.TogglePlayPause();
        }
    }
}
