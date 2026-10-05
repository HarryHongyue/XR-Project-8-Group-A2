using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Project8.UI
{
    /// <summary>
    /// Free text-to-speech for Rick's narration - no plugin, no model download.
    /// On Windows it shells out to PowerShell's built-in System.Speech engine to render
    /// a WAV into the temp cache, then plays it through an AudioSource. The voice is
    /// matched to the selected UI language (en-US and zh-CN voices ship with Windows;
    /// Dutch needs the NL language pack - otherwise the default voice is used).
    /// Windows only; silently disabled elsewhere.
    /// Note: Whisper is speech-to-text, so it cannot produce narration audio.
    /// </summary>
    public class RickVoice : MonoBehaviour
    {
        [Range(0, 100)] public int volume = 90;
        [Range(-10, 10)] public int rate = -1;
        public bool muted;

        AudioSource _source;
        int _job;
        bool _available;

        void Awake()
        {
            _available = Application.platform == RuntimePlatform.WindowsEditor ||
                         Application.platform == RuntimePlatform.WindowsPlayer;
            _source = gameObject.AddComponent<AudioSource>();
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
        }

        /// <summary>Speak the given text in the selected language, replacing current speech.</summary>
        public void Speak(string text)
        {
            if (muted || !_available || string.IsNullOrWhiteSpace(text)) return;
            int colon = text.IndexOf(':');
            if (colon < 0) colon = text.IndexOf('：');
            string spoken = colon >= 0 && colon < 12 ? text.Substring(colon + 1).Trim() : text;
            StartCoroutine(RenderAndPlay(spoken));
        }

        IEnumerator RenderAndPlay(string text)
        {
            int job = ++_job;
            if (_source.isPlaying) _source.Stop();

            string dir = Application.temporaryCachePath;
            string txt = Path.Combine(dir, $"rick_{job}.txt");
            string wav = Path.Combine(dir, $"rick_{job}.wav");
            File.WriteAllText(txt, text);

            string iso = ExperienceSettings.IsChinese ? "zh"
                : ExperienceSettings.IsDutch ? "nl" : "en";
            string script =
                "Add-Type -AssemblyName System.Speech; " +
                "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                "$v = $s.GetInstalledVoices() | Where-Object " +
                "{$_.VoiceInfo.Culture.TwoLetterISOLanguageName -eq '" + iso + "'} | " +
                "Select-Object -First 1; " +
                "if ($v) { $s.SelectVoice($v.VoiceInfo.Name) }; " +
                "$s.Rate = " + rate + "; $s.Volume = " + volume + "; " +
                "$s.SetOutputToWaveFile('" + wav.Replace("'", "''") + "'); " +
                "$s.Speak([IO.File]::ReadAllText('" + txt.Replace("'", "''") +
                "', [Text.Encoding]::UTF8)); $s.Dispose()";

            bool done = false;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // EncodedCommand (UTF-16LE Base64) avoids all command-line quoting issues.
                    string encoded = System.Convert.ToBase64String(
                        System.Text.Encoding.Unicode.GetBytes(script));
                    var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                        "-NoProfile -NonInteractive -EncodedCommand " + encoded)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    var p = System.Diagnostics.Process.Start(psi);
                    p.WaitForExit(30000);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[Project8] RickVoice speech failed: " + e.Message);
                }
                done = true;
            });
            while (!done) yield return null;

            if (job != _job || !File.Exists(wav)) yield break; // a newer line took over
            using (var req = UnityWebRequestMultimedia.GetAudioClip(
                       "file:///" + wav.Replace('\\', '/'), AudioType.WAV))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success || job != _job) yield break;
                _source.clip = DownloadHandlerAudioClip.GetContent(req);
                _source.Play();
            }
        }
    }
}
