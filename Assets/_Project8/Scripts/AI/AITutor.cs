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
            req.timeout = 60; // free-tier NIM cold-starts a model - first call can take ~40s
            yield return req.SendWebRequest();

            string answer = null;
            if (req.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<ChatResponse>(req.downloadHandler.text);
                if (response != null && response.choices != null && response.choices.Length > 0 &&
                    response.choices[0].message != null)
                    answer = response.choices[0].message.content;
            }
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
        [Tooltip("Default: a local Ollama server - free, no API key. For OpenAI/Groq/etc. " +
                 "use e.g. https://api.openai.com/v1/chat/completions plus an API key.")]
        public string endpoint = "http://localhost:11434/v1/chat/completions";
        public string model = "llama3.2";
        [Tooltip("Environment variable that holds the API key (not needed for Ollama). " +
                 "Never commit a key to the repo.")]
        public string apiKeyEnvironmentVariable = "OPENAI_API_KEY";
        [Tooltip("Azure OpenAI sends the key as an 'api-key' header instead of Bearer auth.")]
        public bool azureStyleAuth;
        [TextArea(2, 4)]
        public string systemPrompt =
            "You are a physics tutor inside a VR lesson about gravitational waves. " +
            "The app shows a deliberately simplified model: h(t)=h0 sin(2*pi*f*t+phi), " +
            "transverse stretch/squeeze of test masses, slowed visualization speed and " +
            "exaggerated deformation. Always clarify what is physical vs. a visualization choice.";

        [Header("Events")]
        public UnityEvent<string> onAnswer = new UnityEvent<string>();

        ITutorBackend _backend;
        ITutorBackend _local;

        void Awake()
        {
            _local = new LocalTutorBackend(knowledgeBase);
            if (useRemoteBackend && !string.IsNullOrEmpty(endpoint))
            {
                string key = System.Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
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

        /// <summary>Ask the tutor; the answer arrives via onAnswer. Falls back to the
        /// local knowledge base when the remote endpoint is unreachable.</summary>
        public void Ask(string question)
        {
            Evaluation.ResearchLogger.Instance?.LogEvent("TutorQuestion", question ?? "");
            if (_backend == _local)
            {
                _local.Ask(question, Emit);
                return;
            }
            _backend.Ask(question, answer =>
            {
                if (answer == null) _local.Ask(question, Emit);
                else Emit(answer);
            });
        }

        void Emit(string answer)
        {
            Evaluation.ResearchLogger.Instance?.LogEvent("TutorAnswer", answer ?? "");
            onAnswer.Invoke(answer);
        }
    }
}
