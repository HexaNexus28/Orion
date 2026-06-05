# ORION — Brief Technique pour Kimi (Windsurf)

## Contexte projet
Assistant IA personnel ORION — .NET 9 + React 19 + Vite + Supabase + Ollama/Kimi.
Repo : https://github.com/HexaNexus28/Orion

**LLM utilisé : Kimi K2 via Moonshot cloud (pas Ollama local)**
Config actuelle dans `OllamaOptions.cs` : `Model = "qwen2.5:7b"`, `FallbackModel = "kimi-k2.5:cloud"`
→ Kimi K2 cloud est le modèle principal, qwen est le fallback local.

---

## Problème principal — Latence voix trop longue

Le pipeline voix actuel est séquentiel et bloquant :

```
Frontend enregistre audio
    → POST /api/voice/transcribe   (attend Whisper complet)
    → POST /api/chat/stream        (attend LLM complet)
    → POST /api/voice/synthesize   (attend Kokoro complet)
    → Frontend joue l'audio
```

3 allers-retours réseau + attentes complètes = 4 à 8 secondes de silence.

**De plus**, dans `ChatService.cs`, `StreamMessageAsync` simule le streaming
avec `Task.Delay(50)` artificiel — ce n'est pas du vrai streaming LLM.

---

## Objectif
Créer un pipeline voix en streaming bout-en-bout :
```
Audio → Whisper STT → LLM stream → Kokoro TTS chunk par chunk → Audio stream
Latence perçue cible : ~800ms à 1.5s
```

---

## Fichiers à modifier — dans cet ordre

---

### 1. `backend/Orion.Core/Interfaces/Agents/IConversationAgent.cs`

Ajouter `StreamAsync` à l'interface :

```csharp
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Agents;

public interface IConversationAgent
{
    Task<ApiResponse<ChatResponse>> ProcessAsync(ChatRequest request, CancellationToken ct = default);

    // AJOUTER — vrai streaming LLM token par token
    IAsyncEnumerable<string> StreamAsync(ChatRequest request, CancellationToken ct = default);
}
```

---

### 2. `backend/Orion.Business/Agents/ConversationAgent.cs`

Ajouter les imports en haut :
```csharp
using System.Text;
using System.Threading.Channels;
```

Ajouter la méthode `StreamAsync` à la fin de la classe
(après `ProcessAsync`, même logique de setup mais appelle `StreamAsync` du router) :

```csharp
public async IAsyncEnumerable<string> StreamAsync(
    ChatRequest request,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
{
    // 1. Get or create conversation
    Conversation conversation;
    if (request.SessionId.HasValue)
    {
        conversation = await _unitOfWork.Conversations.GetByIdAsync(request.SessionId.Value, ct)
            ?? new Conversation
            {
                Id = Guid.NewGuid(),
                Type = ConversationType.Chat,
                StartedAt = DateTime.UtcNow,
                LlmProvider = _llmRouter.ActiveProvider
            };
    }
    else
    {
        conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Chat,
            StartedAt = DateTime.UtcNow,
            LlmProvider = _llmRouter.ActiveProvider
        };
        await _unitOfWork.Conversations.AddAsync(conversation, ct);
    }

    // 2. Save user message
    var userMessage = new Message
    {
        Id = Guid.NewGuid(),
        ConversationId = conversation.Id,
        Role = MessageRole.User,
        Content = request.Message,
        CreatedAt = DateTime.UtcNow
    };
    await _unitOfWork.Messages.AddAsync(userMessage, ct);

    // 3. Build message history
    var recentMessages = await _unitOfWork.Messages.GetByConversationIdAsync(conversation.Id, ct);
    var messageHistory = recentMessages.TakeLast(9).Select(m => new LLMMessage
    {
        Role = m.Role.ToString().ToLower(),
        Content = m.Content
    }).ToList();
    messageHistory.Add(new LLMMessage { Role = "user", Content = request.Message });

    // 4. Build tools
    var allTools = _toolRegistry.GetAllTools().ToList();
    var toolDefinitions = allTools.Select(t => new ToolDefinition
    {
        Name = t.Name,
        Description = t.Description,
        Parameters = t.InputSchema
    }).ToList();

    // 5. Build system prompt
    var systemPrompt = _promptBuilder.BuildSystemPrompt(
        new Dictionary<string, string> { ["name"] = "User" },
        new List<MemoryVector>(),
        new List<ToolCallDto>(),
        daemonConnected: _daemonClient.IsConnected,
        _llmRouter.ActiveProvider);

    // 6. Build LLM request
    var llmRequest = new LLMRequest
    {
        SystemPrompt = systemPrompt,
        Messages = messageHistory,
        Temperature = 0.7f,
        Tools = toolDefinitions.Count > 0 ? toolDefinitions : null
    };

    // 7. Stream via channel pour pouvoir yield depuis un callback async
    var fullResponse = new StringBuilder();
    var channel = Channel.CreateUnbounded<string>();

    var streamTask = _llmRouter.StreamAsync(llmRequest, async chunk =>
    {
        fullResponse.Append(chunk);
        await channel.Writer.WriteAsync(chunk, ct);
    }, ct).ContinueWith(t =>
    {
        channel.Writer.Complete(t.IsFaulted ? t.Exception : null);
    }, TaskScheduler.Default);

    await foreach (var chunk in channel.Reader.ReadAllAsync(ct))
    {
        yield return chunk;
    }

    await streamTask;

    // 8. Save assistant response
    var assistantMessage = new Message
    {
        Id = Guid.NewGuid(),
        ConversationId = conversation.Id,
        Role = MessageRole.Assistant,
        Content = fullResponse.ToString(),
        CreatedAt = DateTime.UtcNow
    };
    await _unitOfWork.Messages.AddAsync(assistantMessage, ct);
    await _unitOfWork.SaveChangesAsync(ct);

    _logger.LogInformation("[ConversationAgent/Stream] Done — {Chars} chars", fullResponse.Length);
}
```

---

### 3. `backend/Orion.Business/Services/ChatService.cs`

Remplacer `StreamMessageAsync` — supprimer le faux streaming avec Task.Delay :

```csharp
public async IAsyncEnumerable<string> StreamMessageAsync(
    ChatRequest request,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
{
    _auditService.SetCorrelationId(Guid.NewGuid().ToString("N"));
    _logger.LogInformation("[ChatService] Streaming message for session {SessionId}", request.SessionId);

    await foreach (var chunk in _conversationAgent.StreamAsync(request, ct))
    {
        yield return chunk;
    }
}
```

---

### 4. `backend/Orion.Api/Controllers/VoiceController.cs`

**4a — Ajouter import en haut :**
```csharp
using System.Text;
using Orion.Core.Interfaces.Agents;
```

**4b — Ajouter `IConversationAgent` dans le constructeur :**
```csharp
private readonly IWhisperService _whisperService;
private readonly VoiceNotificationService _voiceNotification;
private readonly IConversationAgent _conversationAgent; // AJOUTER
private readonly ILogger<VoiceController> _logger;

public VoiceController(
    IWhisperService whisperService,
    VoiceNotificationService voiceNotification,
    IConversationAgent conversationAgent, // AJOUTER
    ILogger<VoiceController> logger)
{
    _whisperService = whisperService;
    _voiceNotification = voiceNotification;
    _conversationAgent = conversationAgent; // AJOUTER
    _logger = logger;
}
```

**4c — Ajouter le nouvel endpoint (après GetStatus) :**
```csharp
/// <summary>
/// Pipeline voix complet en streaming bout-en-bout :
/// Audio → Whisper STT → LLM stream → Kokoro TTS chunk par chunk → Audio stream
/// Latence perçue : ~800ms au lieu de 4-8s
/// Un seul appel réseau au lieu de 3
/// </summary>
[HttpPost("converse")]
[Consumes("multipart/form-data")]
public async Task Converse(
    IFormFile audioFile,
    [FromQuery] string? language = null,
    [FromQuery] string? sessionId = null,
    CancellationToken ct = default)
{
    Response.ContentType = "audio/wav";
    Response.Headers["X-Accel-Buffering"] = "no";
    Response.Headers["Cache-Control"] = "no-cache";

    if (audioFile == null || audioFile.Length == 0)
    {
        Response.StatusCode = 400;
        return;
    }

    try
    {
        // ── Étape 1 : STT ──────────────────────────────────────────────
        _logger.LogInformation("[Voice/Converse] STT start — {Size} bytes", audioFile.Length);
        using var audioStream = audioFile.OpenReadStream();
        var sttResult = await _whisperService.TranscribeAsync(audioStream, language);

        if (!sttResult.IsSuccess || string.IsNullOrEmpty(sttResult.Value))
        {
            _logger.LogWarning("[Voice/Converse] STT failed: {Error}", sttResult.Error);
            Response.StatusCode = 400;
            return;
        }

        var transcript = sttResult.Value;
        _logger.LogInformation("[Voice/Converse] STT done: {Text}", transcript);

        // Envoie le transcript dans le header pour que le frontend puisse l'afficher
        Response.Headers["X-Transcript"] = transcript;

        // ── Étape 2 : LLM stream + TTS phrase par phrase ───────────────
        var buffer = new StringBuilder();
        var request = new ChatRequest
        {
            Message = transcript,
            SessionId = sessionId != null && Guid.TryParse(sessionId, out var sid) ? sid : null
        };

        await foreach (var chunk in _conversationAgent.StreamAsync(request, ct))
        {
            buffer.Append(chunk);

            // Dès qu'une phrase est complète → synthétise et envoie immédiatement
            if (EndsWithSentence(buffer.ToString()))
            {
                await SynthesizeAndFlushAsync(buffer.ToString().Trim(), ct);
                buffer.Clear();
            }
        }

        // Flush le reste du buffer (dernière phrase sans ponctuation finale)
        if (buffer.Length > 0)
            await SynthesizeAndFlushAsync(buffer.ToString().Trim(), ct);
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("[Voice/Converse] Cancelled by client");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[Voice/Converse] Error");
        if (!Response.HasStarted)
            Response.StatusCode = 500;
    }
}

// ── Helpers privés ──────────────────────────────────────────────────────────

private static bool EndsWithSentence(string text)
{
    var t = text.TrimEnd();
    return t.EndsWith('.') || t.EndsWith('?') || t.EndsWith('!')
        || t.EndsWith(',') || t.EndsWith(':') || t.EndsWith('\n');
}

private async Task SynthesizeAndFlushAsync(string text, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(text)) return;

    var wav = await _voiceNotification.SynthesizeAsync(text, ct);
    if (wav == null || wav.Length == 0) return;

    await Response.Body.WriteAsync(wav, ct);
    await Response.Body.FlushAsync(ct);

    _logger.LogDebug("[Voice/Converse] Chunk flushed: '{Preview}' → {Kb}KB",
        text.Length > 30 ? text[..30] + "..." : text,
        wav.Length / 1024);
}
```

---

### 5. `backend/Orion.Business/Services/WhisperService.cs`

Passer du modèle `base` au modèle `small` — meilleure précision sur le français :

```csharp
// AVANT
_modelPath = Path.Combine(AppContext.BaseDirectory, "models", "whisper", "ggml-base.bin");
// ...
await DownloadModelAsync(GgmlType.Base, _modelPath);

// APRÈS
_modelPath = Path.Combine(AppContext.BaseDirectory, "models", "whisper", "ggml-small.bin");
// ...
await DownloadModelAsync(GgmlType.Small, _modelPath);
```

---

### 6. `backend/Orion.Core/Configuration/AnthropicOptions.cs`

Corriger le modèle — "kimi-k2.5:cloud" n'est pas un modèle Anthropic :

```csharp
// AVANT
public string Model { get; set; } = "kimi-k2.5:cloud";

// APRÈS — vrai nom modèle Anthropic (fallback cloud si Kimi indisponible)
public string Model { get; set; } = "claude-sonnet-4-20250514";
```

---

## Côté frontend — useVoice.ts

Remplacer les 3 appels séquentiels par un seul appel `/api/voice/converse`
qui lit l'audio en streaming chunk par chunk :

```typescript
// useVoice.ts — remplacer le pipeline 3 étapes

const converseWithOrion = async (audioBlob: Blob, sessionId?: string) => {
  const formData = new FormData()
  formData.append('audioFile', audioBlob, 'voice.webm')

  const url = sessionId
    ? `${API_URL}/api/voice/converse?sessionId=${sessionId}`
    : `${API_URL}/api/voice/converse`

  const response = await fetch(url, {
    method: 'POST',
    body: formData,
  })

  if (!response.ok || !response.body) return

  // Récupère le transcript depuis le header pour l'afficher dans l'UI
  const transcript = response.headers.get('X-Transcript')
  if (transcript) {
    setLastTranscript(transcript) // afficher dans ResponseText ou SlideInput
  }

  // Lit et joue l'audio chunk par chunk au fur et à mesure
  const reader = response.body.getReader()
  const audioCtx = new AudioContext()
  let nextStartTime = audioCtx.currentTime

  while (true) {
    const { done, value } = await reader.read()
    if (done) break

    try {
      const buffer = await audioCtx.decodeAudioData(value.buffer)
      const source = audioCtx.createBufferSource()
      source.buffer = buffer
      source.connect(audioCtx.destination)
      source.start(nextStartTime)
      nextStartTime += buffer.duration // enchaîne sans gap
    } catch {
      // Chunk WAV partiel — ignore et continue
    }
  }
}
```

---

## Résumé des fichiers touchés

```
backend/Orion.Core/Interfaces/Agents/IConversationAgent.cs   +1 méthode interface
backend/Orion.Business/Agents/ConversationAgent.cs           +1 méthode StreamAsync
backend/Orion.Business/Services/ChatService.cs               StreamMessageAsync corrigé
backend/Orion.Api/Controllers/VoiceController.cs             +endpoint /converse
backend/Orion.Business/Services/WhisperService.cs            base → small
backend/Orion.Core/Configuration/AnthropicOptions.cs         modèle corrigé
frontend/src/hooks/useVoice.ts                               pipeline simplifié
```

## Fichiers qui NE changent PAS
```
Program.cs              IConversationAgent déjà enregistré en DI
ChatController.cs       appelle IChatService, rien ne change
LLMRouter.cs            StreamAsync déjà implémenté
OllamaOptions.cs        garder tel quel — Kimi cloud comme modèle principal
DaemonWebSocketClient   aucun changement
VoiceNotificationService aucun changement
```

---

## Note sur Kimi K2 cloud

`OllamaOptions.Model = "qwen2.5:7b"` et `FallbackModel = "kimi-k2.5:cloud"` —
si Kimi cloud est le modèle principal, vérifier dans `LLMRouter.cs`
que la sélection du modèle prioritaire est correcte selon ta config
(`appsettings.Development.json`).