using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace Project8.AI
{
    /// <summary>Backend abstraction: swap the local knowledge base for a live LLM later.</summary>
    public interface ITutorBackend
    {
        void Ask(string question, System.Action<string> onAnswer);
    }

    /// <summary>Offline backend: keyword retrieval over the curated knowledge base.</summary>
    public class LocalTutorBackend : ITutorBackend
    {
        readonly TutorKnowledgeBase _kb;
        public LocalTutorBackend(TutorKnowledgeBase kb) => _kb = kb;
        public void Ask(string question, System.Action<string> onAnswer)
            => onAnswer?.Invoke(_kb != null ? _kb.FindBest(question) : "No knowledge base assigned.");
    }

    /// <summary>
    /// Optional HTTP backend for an LLM endpoint (OpenAI-compatible chat format).
    /// Configure endpoint/key on AITutor; never hard-code credentials in the repo.
    /// </summary>
    public class HttpTutorBackend : ITutorBackend
    {
        readonly MonoBehaviour _runner;
        readonly string _endpoint, _apiKey, _model, _systemPrompt;
        readonly bool _azureStyle;

        public HttpTutorBackend(MonoBehaviour runner, string endpoint, string apiKey,
                                string model, string systemPrompt, bool azureStyle = false)
        {
            _runner = runner; _endpoint = endpoint; _apiKey = apiKey;
            _model = model; _systemPrompt = systemPrompt; _azureStyle = azureStyle;
        }

        /// <summary>Posts the question; onAnswer gets the reply text, or null on failure.</summary>
        public void Ask(string question, System.Action<string> onAnswer)
            => _runner.StartCoroutine(Post(question, onAnswer));

        IEnumerator Post(string question, System.Action<string> onAnswer)
        {
            string body = JsonUtility.ToJson(new ChatRequest
            {
                model = _model,
                messages = new[]
                {
                    new ChatMessage { role = "system", content = _systemPrompt },
                    new ChatMessage { role = "user", content = question },
                }
            });

            var req = new UnityWebRequest(_endpoint, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(_apiKey))
            {
                // Azure OpenAI uses the "api-key" header; OpenAI-compatible services use Bearer.
                if (_azureStyle) req.SetRequestHeader("api-key", _apiKey);
                else req.SetRequestHeader("Authorization", "Bearer " + _apiKey);
            }
            req.timeout = 30; // Foundry answers in seconds; the old NIM cold-start budget no longer applies
            yield return req.SendWebRequest();

            string answer = null;
            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var response = JsonUtility.FromJson<ChatResponse>(req.downloadHandler.text);
                    if (response != null && response.choices != null && response.choices.Length > 0 &&
                        response.choices[0].message != null)
                        answer = response.choices[0].message.content;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[Project8] Tutor reply JSON parse failed: " + e.Message);
                }
            }
            else
            {
                Debug.LogWarning("[Project8] Tutor remote failed: " + req.error +
                    " (HTTP " + req.responseCode + ") - falling back to local answers.");
            }
            // The callback MUST fire on every outcome - a coroutine that dies
            // before this point leaves AITutor.IsBusy stuck and freezes the
            // scripted narration behind the AI hold for good.
            onAnswer?.Invoke(answer);
        }

        [System.Serializable] class ChatRequest { public string model; public ChatMessage[] messages; }
        [System.Serializable] class ChatMessage { public string role; public string content; }
        [System.Serializable] class ChatResponse { public Choice[] choices; }
        [System.Serializable] class Choice { public ChatMessage message; }
    }

    /// <summary>
    /// The in-scene AI tutor. Users ask questions about the phenomenon; answers come from
    /// the local knowledge base by default, or from a configured LLM endpoint when
    /// useRemoteBackend is enabled (course requirement: AI as a functional component).
    /// </summary>
    public class AITutor : MonoBehaviour
    {
        [Header("Local backend")]
        public TutorKnowledgeBase knowledgeBase;

        [Header("Optional LLM backend (OpenAI-compatible chat API)")]
        public bool useRemoteBackend = true;
        [Tooltip("The team's Azure AI Foundry endpoint - OpenAI-compatible v1 " +
                 "chat completions. Override for Ollama/Groq/OpenAI etc.")]
        public string endpoint =
            "https://omnigent-1-resource.services.ai.azure.com/openai/v1/chat/completions";
        public string model = "gpt-5-mini";
        [Tooltip("Environment variable that holds the API key. " +
                 "Never commit a key to the repo.")]
        public string apiKeyEnvironmentVariable = "FOUNDRY_OPENAI_API_KEY";
        [Tooltip("Azure OpenAI / AI Foundry send the key as an 'api-key' header " +
                 "instead of Bearer auth - ON for the team endpoint.")]
        public bool azureStyleAuth = true;
        [TextArea(2, 4)]
        public string systemPrompt =
            "You are Rick, a friendly physics tutor inside a VR lesson about gravitational " +
            "waves and the Einstein Telescope. RULES: answer in at most 3 short sentences; " +
            "use plain everyday language and one simple analogy (a trampoline, ripples on a " +
            "pond, a rubber sheet); no formulas, no jargon, no markdown; match the user's " +
            "language. The app deliberately slows and exaggerates the wave - if relevant, " +
            "say what is real vs. a visualization choice in one clause.";

        [Header("Events")]
        public UnityEvent<string> onAnswer = new UnityEvent<string>();

        ITutorBackend _backend;
        ITutorBackend _local;

        /// <summary>Environment lookup that ALSO checks the Windows User/Machine
        /// scopes - a Unity instance launched before the vars were set still
        /// picks them up live (same trick as VoiceInteraction.EnvVar).</summary>
        static string EnvVar(string name)
        {
            string v = System.Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(v)) return v;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            v = System.Environment.GetEnvironmentVariable(
                name, System.EnvironmentVariableTarget.User);
            if (!string.IsNullOrEmpty(v)) return v;
            v = System.Environment.GetEnvironmentVariable(
                name, System.EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrEmpty(v)) return v;
#endif
            return null;
        }

        void Awake()
        {
            _local = new LocalTutorBackend(knowledgeBase);
            if (useRemoteBackend && !string.IsNullOrEmpty(endpoint))
            {
                string key = EnvVar(apiKeyEnvironmentVariable);
                if (string.IsNullOrEmpty(key))
                    Debug.LogWarning("[Project8] " + apiKeyEnvironmentVariable +
                        " is not set - tutor answers will use the local knowledge base.");
                _backend = new HttpTutorBackend(this, endpoint, key, model, systemPrompt, azureStyleAuth);
            }
            else
            {
                _backend = _local;
            }
            // Warm-up: the free-tier endpoint cold-starts the model on its first
            // request (~40 s). Fire a trivial call at scene start so the first
            // real question does not look dead behind the "thinking" placeholder.
            if (_backend != _local)
                _backend.Ask("ping", _ => { });
        }

        /// <summary>True between Ask() and the emitted answer - the subtitle
        /// presenter keeps the scripted story paused while a reply is pending.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>Ask the tutor; the answer arrives via onAnswer. Falls back to the
        /// local knowledge base when the remote endpoint is unreachable.</summary>
        public void Ask(string question)
        {
            Evaluation.ResearchLogger.Instance?.LogEvent("TutorQuestion", question ?? "");
            // Every question route (typed input AND voice) funnels here - log it.
            Project8.UI.MissionLogUI.Add("You", question);
            IsBusy = true;
            if (_backend == _local)
            {
                _local.Ask(question, Emit);
                return;
            }
            try
            {
                _backend.Ask(question, answer =>
                {
                    if (answer == null) _local.Ask(question, Emit);
                    else Emit(answer);
                });
            }
            catch (System.Exception e)
            {
                // A backend that throws synchronously (coroutine host disabled,
                // bad URL) must not strand IsBusy - answer locally instead.
                Debug.LogWarning("[Project8] Tutor backend threw: " + e.Message);
                _local.Ask(question, Emit);
            }
        }

        void Emit(string answer)
        {
            IsBusy = false;
            Evaluation.ResearchLogger.Instance?.LogEvent("TutorAnswer", answer ?? "");
            onAnswer.Invoke(answer);
        }
    }
}
