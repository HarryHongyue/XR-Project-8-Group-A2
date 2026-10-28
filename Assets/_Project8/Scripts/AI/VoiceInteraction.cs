using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Project8.UI;

namespace Project8.AI
{
    /// <summary>
    /// Hands-free voice loop for Rick:
    ///
    ///   "Hey Rick" (wake word)  or  MIC button / T key
    ///     -> microphone records until ~1.3 s of silence (max 8 s)
    ///     -> audio is transcribed by the team's Whisper server
    ///        (OpenAI-compatible POST /v1/audio/transcriptions)
    ///     -> the text goes to AITutor exactly like a typed question
    ///     -> the answer comes back as subtitles AND spoken voice.
    ///
    /// Wake-word spotting uses UnityEngine.Windows.Speech.KeywordRecognizer,
    /// which only exists on Windows - everywhere else the MIC button is the
    /// entry point (same flow from there). When no Whisper endpoint is
    /// configured, Windows falls back to the built-in DictationRecognizer.
    ///
    /// Server configuration comes from the inspector or the WHISPER_ENDPOINT /
    /// WHISPER_API_KEY environment variables - no credentials in the repo.
    /// </summary>
    public class VoiceInteraction : MonoBehaviour
    {
        public AITutor tutor;
        public TutorChatUI chat;
        public RickSubtitles subtitles;
        public RickVoice voice;

        [Header("Whisper server (OpenAI-compatible transcription endpoint)")]
        [Tooltip("Full URL, e.g. https://whisper.example.nl/v1/audio/transcriptions . " +
                 "Resolution order: this field -> WHISPER_ENDPOINT env -> " +
                 "WHISPER_BASE_URL env + /v1/audio/transcriptions.")]
        public string whisperEndpoint = "";
        [Tooltip("Model name; empty -> WHISPER_MODEL env -> 'whisper-1'.")]
        public string whisperModel = "";
        [Tooltip("Environment variable that holds the optional bearer token.")]
        public string whisperApiKeyEnvironmentVariable = "WHISPER_API_KEY";

        [Header("Wake word (Windows only - MIC button works everywhere)")]
        public bool wakeWordEnabled = true;
        public string[] wakePhrases = { "hey rick", "hi rick" };

        public bool Listening { get; private set; }
        public bool Busy { get; private set; }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR
        UnityEngine.Windows.Speech.KeywordRecognizer _wake;
        UnityEngine.Windows.Speech.DictationRecognizer _dictation;
#endif

        AudioClip _clip;
        int _clipStartPos;
        readonly float[] _rmsWindow = new float[1024];
        float _silenceSince = -1f;
        float _recordStart;
        const float RecordMaxSeconds = 8f;
        const float SilenceSeconds = 1.3f;
        const float SpeechRms = 0.012f;
        const int SampleRate = 16000;

        const string DefaultEndpoint =
            "https://whisper.omnigent.nl/v1/audio/transcriptions";

        string Endpoint
        {
            get
            {
                if (!string.IsNullOrEmpty(whisperEndpoint)) return whisperEndpoint;
                string env = System.Environment.GetEnvironmentVariable("WHISPER_ENDPOINT");
                if (!string.IsNullOrEmpty(env)) return env;
                string baseUrl = System.Environment.GetEnvironmentVariable("WHISPER_BASE_URL");
                // No trailing slash - the server mounts /v1/realtime-style paths
                // literally and a doubled or trailing slash 500s.
                if (!string.IsNullOrEmpty(baseUrl))
                    return baseUrl.TrimEnd('/') + "/v1/audio/transcriptions";
                return DefaultEndpoint;
            }
        }

        string Model =>
            !string.IsNullOrEmpty(whisperModel) ? whisperModel
            : System.Environment.GetEnvironmentVariable("WHISPER_MODEL") ?? "whisper-1";

        // --------------------------------------------------------- wake word
        void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Microphone))
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Microphone);
#endif
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            if (!wakeWordEnabled) return;
            try
            {
                _wake = new UnityEngine.Windows.Speech.KeywordRecognizer(wakePhrases);
                _wake.OnPhraseRecognized += _ =>
                {
                    if (!Listening && !Busy) BeginListening();
                };
                _wake.Start();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Project8] Wake word unavailable: " + e.Message);
            }
#endif
        }

        /// <summary>MIC button / T key entry - toggles the same listening flow
        /// the wake word triggers.</summary>
        public void ToggleListening()
        {
            if (Listening) EndListening();
            else if (!Busy) BeginListening();
        }

        void BeginListening()
        {
            // Windows allows only ONE speech recognizer at a time, and the mic
            // device itself can be exclusive - park the wake listener while we
            // record or dictate, then bring it back when the turn finishes.
            WakeStop();
            if (!string.IsNullOrEmpty(Endpoint) && Microphone.devices.Length > 0)
            {
                _clip = Microphone.Start(null, false, (int)RecordMaxSeconds + 1, SampleRate);
                if (_clip == null) { Hint("Microphone unavailable."); WakeResume(); return; }
                _clipStartPos = 0;
                _recordStart = Time.unscaledTime;
                _silenceSince = -1f;
                Listening = true;
                Busy = true;
                Hint(ExperienceSettings.IsChinese ? "Rick 在听……" : "Rick is listening...");
                return;
            }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            StartDictation();
#else
            Hint(ExperienceSettings.IsChinese
                ? "语音输入需要配置 Whisper 服务器。"
                : "Voice input needs a Whisper server endpoint.");
#endif
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.tKey.wasPressedThisFrame) ToggleListening();
            // VAD: stop when speech ends in a beat of silence (or on timeout).
            if (!Listening || _clip == null) return;
            int pos = Microphone.GetPosition(null);
            if (pos <= 0) return;
            int n = Mathf.Min(_rmsWindow.Length, pos);
            _clip.GetData(_rmsWindow, pos - n);
            float sum = 0f;
            for (int i = 0; i < n; i++) sum += _rmsWindow[i] * _rmsWindow[i];
            float rms = Mathf.Sqrt(sum / n);
            if (rms > SpeechRms) _silenceSince = -1f;
            else if (_silenceSince < 0f) _silenceSince = Time.unscaledTime;
            bool silentLong = _silenceSince > 0f &&
                Time.unscaledTime - _silenceSince > SilenceSeconds;
            bool tooLong = Time.unscaledTime - _recordStart > RecordMaxSeconds;
            if ((silentLong && Time.unscaledTime - _recordStart > 0.6f) || tooLong)
                EndListening();
        }

        void EndListening()
        {
            if (!Listening) return;
            Listening = false;
            if (Microphone.IsRecording(null)) Microphone.End(null);
            if (_clip == null || _clip.samples == 0) { Busy = false; return; }
            Hint(ExperienceSettings.IsChinese ? "识别中……" : "Transcribing...");
            StartCoroutine(Transcribe(_clip));
            _clip = null;
        }

        IEnumerator Transcribe(AudioClip clip)
        {
            var form = new WWWForm();
            form.AddBinaryData("file", WavUtility.FromAudioClip(clip), "speech.wav", "audio/wav");
            form.AddField("model", Model);
            form.AddField("response_format", "json");
            // Hint the UI language; the server also auto-detects mixed zh/en.
            form.AddField("language", ExperienceSettings.IsChinese ? "zh"
                : ExperienceSettings.IsDutch ? "nl" : "en");
            var req = UnityWebRequest.Post(Endpoint, form);
            string key = System.Environment.GetEnvironmentVariable(
                whisperApiKeyEnvironmentVariable);
            if (!string.IsNullOrEmpty(key))
                req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 30;
            yield return req.SendWebRequest();
            Busy = false;
            WakeResume();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Hint(ExperienceSettings.IsChinese
                    ? "语音识别服务暂时不可用。"
                    : "Speech service unreachable.");
                yield break;
            }
            var parsed = JsonUtility.FromJson<Transcription>(req.downloadHandler.text);
            string text = parsed != null ? parsed.text : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                Hint(ExperienceSettings.IsChinese ? "没有听清，请再说一次。"
                    : "Nothing heard - please try again.");
                yield break;
            }
            AskQuestion(text.Trim());
        }

        [System.Serializable] class Transcription { public string text; }

        // ------------------------------------------------- dictation fallback
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
        void StartDictation()
        {
            if (_dictation == null)
            {
                _dictation = new UnityEngine.Windows.Speech.DictationRecognizer();
                _dictation.DictationResult += (text, c) => AskQuestion(text);
                _dictation.DictationComplete += _ => { Listening = false; Busy = false; WakeResume(); };
                _dictation.DictationError += (e, h) => { Listening = false; Busy = false; WakeResume(); };
            }
            try
            {
                _dictation.Start();
                Listening = true;
                Busy = true;
                Hint(ExperienceSettings.IsChinese ? "Rick 在听……" : "Rick is listening...");
            }
            catch (System.Exception e)
            {
                Listening = false;
                Busy = false;
                WakeResume();
                Debug.LogWarning("[Project8] Dictation failed: " + e.Message);
                Hint(ExperienceSettings.IsChinese
                    ? "语音识别启动失败。"
                    : "Speech recognizer failed to start.");
            }
        }
#endif

        /// <summary>One entry point for every captured question - identical to
        /// typing it into the chat box, so the answer/voice path is shared.</summary>
        void AskQuestion(string question)
        {
            Busy = false;
            WakeResume();
            if (subtitles != null)
                subtitles.ShowAnswer(
                    (ExperienceSettings.IsChinese ? "你：" : "You: ") + question);
            if (tutor != null) tutor.Ask(question);
            if (chat != null)
                chat.SetAnswer(ExperienceSettings.IsChinese ? "Rick：思考中……"
                    : ExperienceSettings.IsDutch ? "Rick: aan het denken..."
                    : "Rick: thinking...");
        }

        void Hint(string message)
        {
            if (subtitles != null) subtitles.ShowAnswer(message);
            else Debug.Log("[Project8] Voice: " + message);
        }

        void WakeStop()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            if (_wake != null && _wake.IsRunning) _wake.Stop();
#endif
        }

        void WakeResume()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            if (_wake != null && !_wake.IsRunning && wakeWordEnabled)
            {
                try { _wake.Start(); } catch (System.Exception) { }
            }
#endif
        }

        void OnDestroy()
        {
            if (Listening) { Listening = false; Microphone.End(null); }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            if (_wake != null) { _wake.Stop(); _wake.Dispose(); }
            if (_dictation != null) _dictation.Dispose();
#endif
        }
    }

    /// <summary>Minimal PCM16 WAV encoder for uploading recorded clips.</summary>
    static class WavUtility
    {
        public static byte[] FromAudioClip(AudioClip clip)
        {
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            int byteCount = samples.Length * 2;
            var wav = new byte[44 + byteCount];
            int hz = clip.frequency;
            int ch = clip.channels;
            void Write(int o, string s) { for (int i = 0; i < s.Length; i++) wav[o + i] = (byte)s[i]; }
            void Int32(int o, int v) { System.BitConverter.GetBytes(v).CopyTo(wav, o); }
            void Int16(int o, short v) { System.BitConverter.GetBytes(v).CopyTo(wav, o); }
            Write(0, "RIFF"); Int32(4, 36 + byteCount); Write(8, "WAVE");
            Write(12, "fmt "); Int32(16, 16); Int16(20, 1);
            Int16(22, (short)ch); Int32(24, hz);
            Int32(28, hz * ch * 2); Int16(32, (short)(ch * 2)); Int16(34, 16);
            Write(36, "data"); Int32(40, byteCount);
            for (int i = 0; i < samples.Length; i++)
            {
                short s = (short)Mathf.Clamp(samples[i] * 32767f, -32768f, 32767f);
                Int16(44 + i * 2, s);
            }
            return wav;
        }
    }
}
