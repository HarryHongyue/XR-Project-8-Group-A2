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
    /// On Android/Quest the platform TextToSpeech engine speaks directly with a
    /// matching Locale (zh / nl / en). Other platforms stay silent.
    /// Note: Whisper is speech-to-text, so it cannot produce narration audio.
    /// </summary>
    public class RickVoice : MonoBehaviour
    {
        [Range(0, 100)] public int volume = 90;
        [Range(-10, 10)] public int rate = 2; // was -1: too slow vs the subtitles
        public bool muted;

        /// <summary>True while TTS is rendering, queued or playing. The Android
        /// path polls TextToSpeech.isSpeaking; it does not use the AudioSource.</summary>
        public bool IsSpeaking
        {
            get
            {
                if (_source != null && _source.isPlaying) return true;
                if (_renderingJob != 0 && _renderingJob == _job) return true;
#if UNITY_ANDROID && !UNITY_EDITOR
                return _androidSpeaking;
#else
                return false;
#endif
            }
        }

        AudioSource _source;
        AudioSource _keepAwake;
        int _job;
        bool _available;
        int _renderingJob;

        void Awake()
        {
            _available = Application.platform == RuntimePlatform.WindowsEditor ||
                         Application.platform == RuntimePlatform.WindowsPlayer;
            _source = gameObject.AddComponent<AudioSource>();
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;

            // Audio endpoints sleep after a few silent seconds; the wake-up
            // then eats the first ~300 ms of the next utterance - heard as
            // every sentence losing its first characters. A zero-gain looping
            // clip keeps the mixer + output device permanently warm.
            _keepAwake = gameObject.AddComponent<AudioSource>();
            _keepAwake.clip = AudioClip.Create("voice-keep-awake", 4410, 1, 44100, false);
            _keepAwake.loop = true;
            _keepAwake.volume = 0.002f;
            _keepAwake.spatialBlend = 0f;
            _keepAwake.playOnAwake = false;
            _keepAwake.Play();
        }

        /// <summary>Hard-stop the current utterance on every platform (the caller
        /// re-issues the page on resume). Bumping _job also invalidates any
        /// in-flight wav render or pending Android text, so a cancelled clip
        /// can never start playing late.</summary>
        public void CancelUtterance()
        {
            _job++;
            _renderingJob = 0;
            if (_source != null && _source.isPlaying) _source.Stop();
#if UNITY_ANDROID && !UNITY_EDITOR
            _pendingAndroidText = null;
            _androidSpeaking = false;
            _androidSpeechGuardUntil = 0f;
            if (_tts != null)
            {
                try { _tts.Call("stop"); }
                catch (System.Exception) { }
            }
#endif
        }

        /// <summary>Speak the given text in the selected language, replacing current speech.</summary>
        public void Speak(string text)
        {
            if (muted || string.IsNullOrWhiteSpace(text)) return;
            // Strip a leading SPEAKER TAG only ("Rick:"/"Rick：") - and only
            // when the head is pure ASCII letters. The old "any colon before
            // index 12" rule beheaded every Chinese sentence containing a
            // mid-sentence ：("奥妙来了：…" lost its first words), which became
            // audible on EVERY page once narration split into per-sentence
            // pages. Colons inside real sentences must survive.
            int colon = text.IndexOf(':');
            if (colon < 0) colon = text.IndexOf('：');
            string spoken = text;
            if (colon > 0 && colon < 12)
            {
                bool asciiName = true;
                for (int i = 0; i < colon; i++)
                {
                    char ch = text[i];
                    if (!((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || ch == ' '))
                    { asciiName = false; break; }
                }
                if (asciiName) spoken = text.Substring(colon + 1).Trim();
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            SpeakAndroid(spoken);
            return;
#else
            if (!_available) return;
            StartCoroutine(RenderAndPlay(spoken));
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // -------------------------------------------------- Android TTS -----
        /// <summary>Quest/Android path: the OS text-to-speech engine speaks
        /// directly - no wav file, no PowerShell. Language follows the UI
        /// language (zh / nl / en) just like the Windows System.Speech path.</summary>
        class TtsInitListener : AndroidJavaProxy
        {
            readonly RickVoice _owner;
            public TtsInitListener(RickVoice owner)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                _owner = owner;
            }
            // ReSharper disable once InconsistentNaming - Android interface signature
            public void onInit(int status) => _owner.OnTtsInitialized(status);
        }

        AndroidJavaObject _tts;
        TtsInitListener _ttsInitListener;
        bool _ttsFailed;
        bool _ttsReady;
        bool _androidSpeaking;
        float _androidSpeechGuardUntil;
        float _nextAndroidPoll;
        string _pendingAndroidText;
        int _pendingAndroidJob;

        void Update()
        {
            if (_tts == null || Time.unscaledTime < _nextAndroidPoll) return;
            _nextAndroidPoll = Time.unscaledTime + 0.1f;
            if (Time.unscaledTime < _androidSpeechGuardUntil) return;
            try
            {
                _androidSpeaking = _tts.Call<bool>("isSpeaking");
            }
            catch (System.Exception e)
            {
                _androidSpeaking = false;
                Debug.LogWarning("[Project8] Android TTS status check failed: " + e.Message);
            }
        }

        void SpeakAndroid(string text)
        {
            if (_ttsFailed) return;
            try
            {
                _pendingAndroidText = text;
                _pendingAndroidJob = ++_job;
                _androidSpeaking = true;
                if (_tts == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        _ttsInitListener = new TtsInitListener(this);
                        _tts = new AndroidJavaObject("android.speech.tts.TextToSpeech",
                            activity, _ttsInitListener);
                    }
                }
                if (_ttsReady) SpeakAndroidReadyText();
            }
            catch (System.Exception e)
            {
                _ttsFailed = true;
                _androidSpeaking = false;
                Debug.LogWarning("[Project8] Android TTS unavailable: " + e.Message);
            }
        }

        void OnTtsInitialized(int status)
        {
            if (status != 0)
            {
                _ttsFailed = true;
                _androidSpeaking = false;
                return;
            }
            _ttsReady = true;
            if (_tts != null && !string.IsNullOrEmpty(_pendingAndroidText))
                SpeakAndroidReadyText();
        }

        void SpeakAndroidReadyText()
        {
            string text = _pendingAndroidText;
            int job = _pendingAndroidJob;
            _pendingAndroidText = null;
            if (string.IsNullOrEmpty(text) || job != _job) return;

            try
            {
                string iso = ExperienceSettings.IsChinese ? "zh"
                    : ExperienceSettings.IsDutch ? "nl" : "en";
                string localeTag = ExperienceSettings.IsChinese ? "zh-CN"
                    : ExperienceSettings.IsDutch ? "nl-NL" : "en-US";
                using (var locale = new AndroidJavaObject("java.util.Locale", localeTag))
                    _tts.Call<int>("setLanguage", locale);

                // Prefer a system voice whose name advertises a male variant;
                // keep the locale-selected system voice if none is installed.
                bool maleVoiceSelected = false;
                string maleVoiceName = "";
                try
                {
                    using (var voices = _tts.Call<AndroidJavaObject>("getVoices"))
                    using (var iterator = voices.Call<AndroidJavaObject>("iterator"))
                    {
                        while (iterator.Call<bool>("hasNext"))
                        {
                            using (var candidate = iterator.Call<AndroidJavaObject>("next"))
                            {
                                string name = candidate.Call<string>("getName");
                                bool sameLanguage = !string.IsNullOrEmpty(name) &&
                                    (iso == "zh"
                                        ? name.IndexOf("zh", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          name.IndexOf("cmn", System.StringComparison.OrdinalIgnoreCase) >= 0
                                        : name.IndexOf(iso, System.StringComparison.OrdinalIgnoreCase) >= 0);
                                bool masculineName = !string.IsNullOrEmpty(name) &&
                                    (name.IndexOf("male", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     name.IndexOf("masculine", System.StringComparison.OrdinalIgnoreCase) >= 0);
                                if (sameLanguage && masculineName)
                                {
                                    _tts.Call<int>("setVoice", candidate);
                                    maleVoiceSelected = true;
                                    maleVoiceName = name;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (System.Exception)
                {
                    // Some Android engines do not expose a selectable male voice.
                }

                // Some Android engines omit gender from voice names; use a
                // noticeably lower pitch rather than the default high voice.
                _tts.Call<int>("setPitch", maleVoiceSelected ? 1f : 0.65f);
                Debug.Log(maleVoiceSelected
                    ? "[Project8] Android male TTS voice: " + maleVoiceName
                    : "[Project8] No named male TTS voice for " + localeTag +
                      "; using lower-pitch system voice.");
                _tts.Call<int>("setSpeechRate", Mathf.Clamp(1f + rate * 0.1f, 0.5f, 2f));
                int result = _tts.Call<int>("speak", text, 0 /* QUEUE_FLUSH */,
                    null, "rick_" + job);
                _androidSpeaking = result >= 0;
                _androidSpeechGuardUntil = Time.unscaledTime + 0.35f;
            }
            catch (System.Exception e)
            {
                _androidSpeaking = false;
                Debug.LogWarning("[Project8] Android TTS playback failed: " + e.Message);
            }
        }

        void OnDestroy()
        {
            if (_tts == null) return;
            try
            {
                _tts.Call("stop");
                _tts.Call("shutdown");
                _tts.Dispose();
            }
            catch (System.Exception) { }
            _tts = null;
            _ttsInitListener = null;
            _androidSpeaking = false;
        }
#endif

        IEnumerator RenderAndPlay(string text)
        {
            int job = ++_job;
            _renderingJob = job;
            if (_source.isPlaying) _source.Stop();

            string dir = Application.temporaryCachePath;
            string txt = Path.Combine(dir, $"rick_{job}.txt");
            string wav = Path.Combine(dir, $"rick_{job}.wav");
            string vname = Path.Combine(dir, $"rick_{job}.voice.txt");
            File.WriteAllText(txt, text);

            string iso = ExperienceSettings.IsChinese ? "zh"
                : ExperienceSettings.IsDutch ? "nl" : "en";
            string culture = ExperienceSettings.IsChinese ? "zh-CN"
                : ExperienceSettings.IsDutch ? "nl-NL" : "en-US";
            // MALE VOICE: voices installed through Windows Settings (OneCore)
            // are invisible to System.Speech/SAPI5 - SAPI only sees the old
            // desktop voices (this machine: two female voices). WinRT's
            // Windows.Media.SpeechSynthesis sees the OneCore set too, which
            // here includes Kangkang (zh-CN male) and Frank (nl-NL male).
            // Priority: same-language male -> ANY male (user wants a male
            // voice in every language; a zh male reading English beats an
            // en female) -> same-language any gender. No <voice> element in
            // the SSML: it would override $synth.Voice with the language
            // default - the exact bug that kept playing the female voice.
            string script =
                "$wav='" + wav.Replace("'", "''") + "'; " +
                "$txt='" + txt.Replace("'", "''") + "'; " +
                "$vname='" + vname.Replace("'", "''") + "'; " +
                "$text=[IO.File]::ReadAllText($txt,[Text.Encoding]::UTF8); " +
                "function W($o){while($o.Status -eq 'Started'){" +
                "Start-Sleep -Milliseconds 10};$o.GetResults()}; " +
                "$ok=$false; " +
                "try{Add-Type -AssemblyName System.Runtime.WindowsRuntime;" +
                "$synth=New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer;" +
                "$all=[Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices;" +
                "$v=$all|Where-Object{$_.Language -like '" + iso +
                "-*' -and $_.Gender -eq 'Male'}|Select-Object -First 1;" +
                "if(-not $v){$v=$all|Where-Object{$_.Gender -eq 'Male'}|" +
                "Select-Object -First 1};" +
                "if(-not $v){$v=$all|Where-Object{$_.Language -like '" + iso +
                "-*'}|Select-Object -First 1};" +
                "if($v){$synth.Voice=$v;" +
                "[IO.File]::WriteAllText($vname,$synth.Voice.DisplayName+'|'+$synth.Voice.Gender+'|'+$synth.Voice.Language);" +
                "$synth.Options.SpeakingRate=" +
                Mathf.Clamp(1f + rate * 0.1f, 0.5f, 2f)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" +
                "$ssml='<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"'+$v.Language+'\">" +
                "<break time=\"350ms\"/>'+[Security.SecurityElement]::Escape($text)+'</speak>';" +
                "try{$st=W($synth.SynthesizeSsmlToStreamAsync($ssml))}catch{" +
                "$st=W($synth.SynthesizeTextToStreamAsync($text))};" +
                "$reader=New-Object Windows.Storage.Streams.DataReader -ArgumentList $st.GetInputStreamAt(0);" +
                "$null=W($reader.LoadAsync([uint32]$st.Size));" +
                "$bytes=New-Object byte[] $st.Size;$reader.ReadBytes($bytes);" +
                "[IO.File]::WriteAllBytes($wav,$bytes);$ok=$true}}catch{$ok=$false} " +
                "if(-not $ok){" +
                "Add-Type -AssemblyName System.Speech;" +
                "$s=New-Object System.Speech.Synthesis.SpeechSynthesizer;" +
                "$vs=$s.GetInstalledVoices()|Where-Object{" +
                "$_.VoiceInfo.Culture.TwoLetterISOLanguageName -eq '" + iso + "'};" +
                "$m=$vs|Where-Object{$_.VoiceInfo.Gender.ToString() -eq 'Male'}|" +
                "Select-Object -First 1;" +
                "if(-not $m){$m=$vs|Select-Object -First 1};" +
                "if($m){$s.SelectVoice($m.VoiceInfo.Name)};" +
                "$s.Rate=" + rate + ";$s.Volume=" + volume + ";" +
                "$s.SetOutputToWaveFile($wav);" +
                // SAPI5 fallback (English on this PC): no male voice exists,
                // so deepen whatever voice is left with SSML prosody. The
                // leading 350 ms break absorbs audio-device wake-up latency.
                "$ssml2='<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"" +
                culture + "\"><break time=\"350ms\"/><prosody pitch=\"-18%\">'+" +
                "[Security.SecurityElement]::Escape($text)+'</prosody></speak>';" +
                "try{$s.SpeakSsml($ssml2)}catch{" +
                "$pb=New-Object System.Speech.Synthesis.PromptBuilder;" +
                "$pb.AppendBreak([TimeSpan]::FromMilliseconds(350));" +
                "$pb.AppendText($text);$s.Speak($pb)};" +
                "$s.Dispose()}";

            bool done = false;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // EncodedCommand (UTF-16LE Base64) avoids all command-line quoting issues.
                    string encoded = System.Convert.ToBase64String(
                        System.Text.Encoding.Unicode.GetBytes(script));
                    // powershell.exe is not guaranteed to be on PATH on every
                    // machine - fall back to the absolute System32 location.
                    string shell = "powershell.exe";
                    try
                    {
                        var probe = new System.Diagnostics.ProcessStartInfo(shell,
                            "-NoProfile -Command exit 0")
                        { CreateNoWindow = true, UseShellExecute = false };
                        using (var p0 = System.Diagnostics.Process.Start(probe)) p0.WaitForExit(3000);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        shell = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
                    }
                    var psi = new System.Diagnostics.ProcessStartInfo(shell,
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

            if (job != _job || !File.Exists(wav))
            {
                if (_renderingJob == job) _renderingJob = 0;
                yield break;
            }
            if (File.Exists(vname))
                Debug.Log("[Project8] Rick voice: " + File.ReadAllText(vname));
            using (var req = UnityWebRequestMultimedia.GetAudioClip(
                       "file:///" + wav.Replace('\\', '/'), AudioType.WAV))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success || job != _job)
                {
                    if (_renderingJob == job) _renderingJob = 0;
                    yield break;
                }
                _source.clip = DownloadHandlerAudioClip.GetContent(req);
                _source.Play();
                if (_renderingJob == job) _renderingJob = 0;
            }
        }
    }
}
