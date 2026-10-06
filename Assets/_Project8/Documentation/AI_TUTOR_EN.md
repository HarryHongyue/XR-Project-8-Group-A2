# AI Tutor (Rick) — How It Works

> Audience: team members / assessors. No third-party LLM plugin is used.

## 1. The big picture

The AI tutor is a **plain C# script** that sends HTTP requests. There is no
LLMUnity, no OpenAI Unity plugin, nothing extra installed — only Unity's
built-in `UnityWebRequest`.

```
User types a question in the panel
        │
        ▼
TutorChatUI.AskTutor()                      Scripts/AI/TutorChatUI.cs
        │  tutor.Ask(question)
        ▼
AITutor                                   Scripts/AI/AITutor.cs
        │  useRemoteBackend = true ?
        ├─ yes ─► HttpTutorBackend  ── HTTP POST ──► LLM server (Ollama / NVIDIA / Azure / Groq)
        │              │ answer received?                    │
        │              ├─ yes ─► Emit(answer) ◄──────────────┘
        │              └─ no  ─► LocalTutorBackend (offline fallback)
        └─ no  ─► LocalTutorBackend
        │
        ▼
onAnswer.Invoke(text)  ─► TutorChatUI.ShowAnswer()  ─► displayed in the panel
                       ─► RickVoice.Speak()          ─► read aloud (TTS)
                       ─► ResearchLogger             ─► logged for evaluation
```

## 2. The three pieces of code

### `ITutorBackend` — the abstraction

```csharp
public interface ITutorBackend {
    void Ask(string question, System.Action<string> onAnswer);
}
```

Two implementations:

| Implementation | What it does |
|---|---|
| `LocalTutorBackend` | Keyword search over `TutorKnowledgeBase` — a curated list of Q&A entries. Works offline, zero cost, always available. |
| `HttpTutorBackend` | Sends the question to a real LLM over HTTP, parses the JSON reply. |

### `HttpTutorBackend` — the HTTP client

For each question it builds the **OpenAI-compatible chat request** format:

```json
POST {endpoint}
Content-Type: application/json
Authorization: Bearer {apiKey}          // or "api-key: {key}" for Azure

{
  "model": "meta/llama-3.1-8b-instruct",
  "messages": [
    { "role": "system", "content": "You are a physics tutor inside a VR lesson..." },
    { "role": "user",   "content": "what is a gravitational wave?" }
  ]
}
```

The reply is parsed from the standard field `choices[0].message.content`.
If the request fails (server down, bad key, timeout = 20 s), `Ask` returns
`null` and `AITutor` silently retries with the local knowledge base — so the
demo **can never break** in front of an audience.

### `AITutor` — the MonoBehaviour in the scene

A GameObject named `AITutor` exists in `Main.unity`. Its Inspector fields are
the entire configuration surface:

| Inspector field | Meaning |
|---|---|
| `knowledgeBase` | Reference to the `TutorKnowledgeBase` asset (local fallback) |
| `useRemoteBackend` | Master switch: true = try HTTP first, false = always local |
| `endpoint` | URL the request is POSTed to |
| `model` | Model name sent inside the JSON body |
| `apiKeyEnvironmentVariable` | **Name** of an environment variable that holds the key — the key itself is never stored in the project |
| `azureStyleAuth` | Sends `api-key:` header instead of `Authorization: Bearer` (Microsoft Azure only) |
| `systemPrompt` | System message — tells the model it's a physics tutor and explains the app's deliberate simplifications |
| `onAnswer` | `UnityEvent<string>` wired to `TutorChatUI.ShowAnswer` and `RickVoice.Speak` |

## 3. Why the environment variable?

API keys must never be committed to git. So the scene stores **only the name**
of the variable (e.g. `OPENAI_API_KEY`). At runtime:

```csharp
string key = System.Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
```

If the variable isn't set, the request is still sent — Ollama ignores auth —
but NVIDIA/OpenAI/etc. will return 401 and the tutor falls back to the local
knowledge base.

## 4. Wiring it to NVIDIA NIM (free tier)

1. Get a key at <https://build.nvidia.com> — sign in, open any model page,
   click **Get API Key**.
2. Set the environment variable on Windows (PowerShell, user-level, no admin):

   ```powershell
   [Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "nvapi-xxxxxxxx", "User")
   ```

   Then **restart Unity** (env vars are read when the process starts).

3. In `Main.unity`, select the **AITutor** GameObject and set:
   - `endpoint` = `https://integrate.api.nvidia.com/v1/chat/completions`
   - `model` = `meta/llama-3.1-8b-instruct` (or any model on the NIM catalog)
   - `useRemoteBackend` = ✔
   - `azureStyleAuth` = ✘ (NVIDIA uses Bearer auth)

4. Play → open CONTROLS panel → type a question in ASK RICK → the answer
   comes from the live model and is read aloud by Rick's voice.

### Other providers — same mechanism, different URL

| Provider | `endpoint` | key needed? | `azureStyleAuth` |
|---|---|---|---|
| Ollama (local) | `http://localhost:11434/v1/chat/completions` | no | off |
| NVIDIA NIM | `https://integrate.api.nvidia.com/v1/chat/completions` | `nvapi-…` | off |
| Groq | `https://api.groq.com/openai/v1/chat/completions` | `gsk_…` | off |
| Azure OpenAI | `https://{resource}.openai.azure.com/openai/deployments/{name}/chat/completions?api-version=2024-02-01` | Azure key | **on** |
| OpenAI | `https://api.openai.com/v1/chat/completions` | `sk-…` | off |

## 5. What "functional AI component" means for the rubric

- The AI isn't decorative: users can ask **free-text questions** and get
  contextual answers.
- The system prompt pins the model to the physics content and makes it
  state explicitly which parts of the visualization are real physics vs.
  pedagogical exaggeration.
- Every question and answer is written to `ResearchLogger`, so the AI
  interaction is measurable in the evaluation data.
- Offline fallback keeps the demo robust without internet.
