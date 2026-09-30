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

        public HttpTutorBackend(MonoBehaviour runner, string endpoint, string apiKey,
                                string model, string systemPrompt)
        {
            _runner = runner; _endpoint = endpoint; _apiKey = apiKey;
            _model = model; _systemPrompt = systemPrompt;
        }

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
            // NOTE: JsonUtility cannot serialize nested arrays for some providers;
            // for production use a real JSON library or hand-built payload.

            var req = new UnityWebRequest(_endpoint, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(_apiKey))
                req.SetRequestHeader("Authorization", "Bearer " + _apiKey);
            req.timeout = 15;
            yield return req.SendWebRequest();
            onAnswer?.Invoke(req.result == UnityWebRequest.Result.Success
                ? req.downloadHandler.text
                : "The tutor is offline right now - try the built-in explanation instead.");
        }

        [System.Serializable] class ChatRequest { public string model; public ChatMessage[] messages; }
        [System.Serializable] class ChatMessage { public string role; public string content; }
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

        [Header("Optional LLM backend")]
        public bool useRemoteBackend;
        public string endpoint = "https://api.openai.com/v1/chat/completions";
        public string model = "gpt-4o-mini";
        [Tooltip("Read from environment variable OPENAI_API_KEY at runtime - never commit a key.")]
        public string apiKeyEnvironmentVariable = "OPENAI_API_KEY";
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
                _backend = new HttpTutorBackend(this, endpoint, key, model, systemPrompt);
            }
            else
            {
                _backend = _local;
            }
        }

        /// <summary>Ask the tutor; the answer arrives via onAnswer.</summary>
        public void Ask(string question)
        {
            Evaluation.ResearchLogger.Instance?.LogEvent("TutorQuestion", question ?? "");
            _backend.Ask(question, answer =>
            {
                Evaluation.ResearchLogger.Instance?.LogEvent("TutorAnswer", answer ?? "");
                onAnswer.Invoke(answer);
            });
        }
    }
}
