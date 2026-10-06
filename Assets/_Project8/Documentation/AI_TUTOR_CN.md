# AI 导师（Rick）—— 实现原理说明

> 面向同学 / 评审。**没有使用任何第三方 LLM 插件**（没有 LLMUnity、没有
> UnityLLM），全部依赖 Unity 自带的 `UnityWebRequest` 发 HTTP 请求。

## 1. 整体流程

```
用户在面板里输入问题
        │
        ▼
TutorChatUI.AskTutor()                      Scripts/AI/TutorChatUI.cs
        │  tutor.Ask(question)
        ▼
AITutor                                   Scripts/AI/AITutor.cs
        │  useRemoteBackend = true ?
        ├─ 是 ─► HttpTutorBackend ── HTTP POST ──► LLM 服务器（Ollama / NVIDIA / Azure / Groq）
        │            │ 收到回答？                          │
        │            ├─ 是 ─► Emit(answer) ◄───────────────┘
        │            └─ 否 ─► LocalTutorBackend（本地兜底）
        └─ 否 ─► LocalTutorBackend
        │
        ▼
onAnswer.Invoke(text)  ─► TutorChatUI.ShowAnswer()  ─► 显示在面板上
                       ─► RickVoice.Speak()          ─► 语音朗读（TTS）
                       ─► ResearchLogger             ─► 写入评估日志
```

## 2. 三段代码

### `ITutorBackend` —— 抽象接口

```csharp
public interface ITutorBackend {
    void Ask(string question, System.Action<string> onAnswer);
}
```

两个实现：

| 实现类 | 作用 |
|---|---|
| `LocalTutorBackend` | 在 `TutorKnowledgeBase` 里做关键词检索——内置问答库，离线可用、零成本，永远兜底。 |
| `HttpTutorBackend` | 把问题通过 HTTP 发给真正的 LLM，解析返回的 JSON。 |

### `HttpTutorBackend` —— HTTP 客户端

每个问题都构造一个 **OpenAI 标准格式的请求**：

```json
POST {endpoint}
Content-Type: application/json
Authorization: Bearer {apiKey}          // Azure 则用 "api-key: {key}"

{
  "model": "openai/gpt-oss-120b",
  "messages": [
    { "role": "system", "content": "You are a physics tutor inside a VR lesson..." },
    { "role": "user",   "content": "what is a gravitational wave?" }
  ]
}
```

返回时从标准字段 `choices[0].message.content` 取回答。
请求失败（服务器挂了、key 错、20 秒超时）时 `Ask` 返回 `null`，
`AITutor` 自动改用本地知识库回答——**演示永远不会因为断网崩掉**。

### `AITutor` —— 场景里的组件

`Main.unity` 里有个叫 `AITutor` 的 GameObject，Inspector 上的字段就是
全部配置项：

| Inspector 字段 | 含义 |
|---|---|
| `knowledgeBase` | `TutorKnowledgeBase` 资产引用（本地兜底库） |
| `useRemoteBackend` | 总开关：true = 先走 HTTP，false = 只用本地 |
| `endpoint` | 请求 POST 到的 URL |
| `model` | JSON body 里的模型名 |
| `apiKeyEnvironmentVariable` | 存 key 的**环境变量名**——key 本身不存进项目 |
| `azureStyleAuth` | 改用 `api-key:` 头发送（仅微软 Azure 需要） |
| `systemPrompt` | 系统提示词——告诉模型它是物理导师，并说明可视化的刻意简化 |
| `onAnswer` | `UnityEvent<string>`，已绑定 `TutorChatUI.ShowAnswer` 和 `RickVoice.Speak` |

## 3. 为什么用环境变量存 key？

API key 绝不能提交进 git，所以场景里只存**变量名**
（比如 `OPENAI_API_KEY`）。运行时读取：

```csharp
string key = System.Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
```

如果变量没设置，请求照样发——Ollama 不验证 auth——但 NVIDIA/OpenAI
会返回 401，这时自动回退到本地知识库。

## 4. 接入 NVIDIA NIM（免费额度）具体步骤

1. 到 <https://build.nvidia.com> 登录，打开任意模型页面，点
   **Get API Key** 拿到 `nvapi-xxxxxxxx`。
2. 在 Windows PowerShell 里设置环境变量（用户级，不需要管理员）：

   ```powershell
   [Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "nvapi-xxxxxxxx", "User")
   ```

   设完后**重启 Unity**（环境变量在进程启动时读取）。

3. 在 `Main.unity` 里选中 **AITutor** 对象，Inspector 里改三个字段：
   - `endpoint` = `https://integrate.api.nvidia.com/v1/chat/completions`
   - `model` = `openai/gpt-oss-120b`（或 NIM 目录里任意模型）
   - `useRemoteBackend` = ✔
   - `azureStyleAuth` = ✘（NVIDIA 用 Bearer 认证，**不要勾**）

4. Play → 打开 CONTROLS 面板 → 在 ASK RICK 输入问题 → 回答来自真实的
   在线模型，并且 Rick 会语音朗读。

### 其他服务商——同一套机制，只换 URL

| 服务 | `endpoint` | 需要 key？ | `azureStyleAuth` |
|---|---|---|---|
| Ollama（本地） | `http://localhost:11434/v1/chat/completions` | 否 | 不勾 |
| NVIDIA NIM | `https://integrate.api.nvidia.com/v1/chat/completions` | `nvapi-…` | 不勾 |
| Groq | `https://api.groq.com/openai/v1/chat/completions` | `gsk_…` | 不勾 |
| Azure OpenAI | `https://{资源名}.openai.azure.com/openai/deployments/{部署名}/chat/completions?api-version=2024-02-01` | Azure key | **勾** |
| OpenAI | `https://api.openai.com/v1/chat/completions` | `sk-…` | 不勾 |

## 5. 对课程评分点的意义

- AI 不是摆设：用户可以输入**任意问题**并获得有上下文的回答。
- system prompt 把模型约束在物理内容上，并要求它明确区分"真实物理"
  和"为了教学做的可视化夸张"。
- 每个问题和回答都写入 `ResearchLogger`，AI 交互是可量化的评估数据。
- 离线兜底保证断网时演示不崩。
