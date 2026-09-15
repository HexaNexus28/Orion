# ORION - Flux et Séquences

## Diagramme de Séquence — Requête Chat (streamée, avec outils)

Le flux réel est **streamé et agentique** : la réponse part token par token, et le modèle peut
décider d'appeler un outil au milieu. Une séquence « requête → réponse complète » ne décrirait
plus rien de ce qui se passe.

```mermaid
sequenceDiagram
    participant Client
    participant ChatController
    participant ConversationAgent
    participant IEmbeddingService
    participant AgentLoop
    participant LLMCascade
    participant IToolInvoker
    participant IMemoryService
    participant UnitOfWork

    Client->>ChatController: POST /api/chat/stream {message}
    ChatController->>ConversationAgent: StreamAsync(request, ct)

    Note over ConversationAgent,IEmbeddingService: Contexte — souvenirs proches de la demande
    ConversationAgent->>IEmbeddingService: EmbedAsync(userMessage)
    IEmbeddingService-->>ConversationAgent: vecteur
    ConversationAgent->>UnitOfWork: Memory.SearchAsync(vecteur)
    UnitOfWork-->>ConversationAgent: souvenirs

    ConversationAgent->>AgentLoop: RunAsync(request, invokeTool, ct)

    loop Tant que le modèle demande un outil
        AgentLoop->>LLMCascade: StreamAsync(request, tools, ct)
        Note over LLMCascade: NIM d'abord, Ollama local en repli
        LLMCascade-->>AgentLoop: tokens / tool_call
        AgentLoop->>IToolInvoker: InvokeAsync(nom, args, ct)
        Note over IToolInvoker: SEUL point qui exécute, diffère (PC éteint) ou REFUSE
        IToolInvoker-->>AgentLoop: ToolOutcome
    end

    AgentLoop-->>ConversationAgent: AgentEvent (Token, ToolCall, …)
    ConversationAgent-->>ChatController: AgentEvent
    ChatController-->>Client: SSE data: … (au fil de l'eau)

    Note over ConversationAgent,IMemoryService: Après le tour — la trace, jamais avant
    ConversationAgent->>UnitOfWork: Messages.AddAsync() + SaveChangesAsync()
    ConversationAgent->>IMemoryService: SaveMemoryAsync(épisode, Episode)
```

Deux invariants se lisent directement sur ce diagramme :

- **Aucune flèche ne va d'un agent vers un client LLM.** Tout passe par `AgentLoop`.
- **Aucune flèche ne va d'un agent vers un outil.** Tout passe par `IToolInvoker` — c'est là, dans
  le code et APRÈS la décision du modèle, que vit le garde-fou. Pas dans une phrase du prompt.

---

## Architecture Hexagonale (Ports & Adapters)

```mermaid
flowchart TB
    subgraph "Presentation Layer (API)"
        C[ChatController]
        H[HealthController]
        V["VoiceWebSocketHandler<br/>/ws/voice"]
    end

    subgraph "Application Services (Business)"
        CS[ChatService]
        LS[LLMService]
        MS[MemoryService]
        BS[BriefingService]
        AS[AuditService]
    end

    subgraph "Agents (Business Internals)"
        CA[ConversationAgent]
        BA[BriefingAgent]
        AL[AgentLoop]
        TI["ToolInvoker<br/>execute | differe | REFUSE"]
        TR[ToolRegistry]
        LC[LLMCascade]
        NIM[NimAgentClient]
        OAC[OllamaAgentClient]
    end

    subgraph "Domain Layer (Core)"
        E[Entities]
        D[DTOs]
        I[Interfaces]
    end

    subgraph "Infrastructure Layer (Data)"
        UoW[UnitOfWork]
        R[Repositories]
        DB[OrionDbContext]
    end

    subgraph "External"
        NV[NVIDIA NIM]
        OLL[Ollama Local]
        SUP[Supabase/pgvector]
        DAE[Daemon Windows]
    end

    C --> CS
    V --> CA
    H --> LS
    CS --> CA
    LS --> LC
    MS --> UoW
    BS --> BA
    CA --> AL
    BA --> AL
    CA --> MS
    CA --> UoW
    AL --> LC
    AL --> TI
    TI --> TR
    TI --> DAE
    LC --> NIM
    LC --> OAC
    NIM --> NV
    OAC --> OLL
    UoW --> R
    R --> DB
    DB --> SUP

    CS -.-> I
    LS -.-> I
    CA -.-> I
    AL -.-> I
    UoW -.-> I
    R -.-> E
```

⚠️ **Ce diagramme a longtemps montré `MemoryAgent`, `ToolAgent` et `AnthropicClient`.** Aucun des
trois n'a jamais existé dans le dépôt. Un schéma qui invente des classes est pire qu'un schéma
absent : on cherche le fichier, on ne le trouve pas, et on conclut qu'on a mal lu.

### Flux de données

1. **Controller / WebSocket** reçoit la requête
2. **Service** orchestre la logique métier
3. **Agent** construit le contexte (souvenirs, profil, prompt) et délègue à `AgentLoop`
4. **AgentLoop** est le SEUL à parler au modèle ; **ToolInvoker** le seul à exécuter un outil
5. **Repository** persiste les données
6. **External** (NIM → Ollama en cascade, Supabase, daemon) fournit les ressources

---

## Pattern de Retour par Couche

```
┌─────────────────────────────────────────────────────────┐
│  Couche          │  Type de retour     │  Exemple      │
├─────────────────────────────────────────────────────────┤
│  Data            │  T? / IEnumerable   │  Conversation  │
│  Business        │  ApiResponse<T>     │  ApiResponse   │
│  API (Controller)│  IActionResult      │  StatusCode()  │
└─────────────────────────────────────────────────────────┘
```

### Exemple

**Business (Service)**
```csharp
public async Task<ApiResponse<ChatResponse>> SendMessageAsync(ChatRequest request)
{
    var conv = await _unitOfWork.Conversations.GetByIdAsync(request.SessionId);
    if (conv is null)
        return ApiResponse<ChatResponse>.NotFoundResponse("Session introuvable");
    
    // ... process ...
    
    return ApiResponse<ChatResponse>.SuccessResponse(new ChatResponse { ... });
}
```

**Controller**
```csharp
[HttpPost]
public async Task<IActionResult> Chat([FromBody] ChatRequest request)
{
    var response = await _chatService.SendMessageAsync(request);
    return StatusCode(response.StatusCode, response);  // Unwrap uniquement
}
```

---

## ToolResult vs ApiResponse

| Aspect | ToolResult | ApiResponse<T> |
|--------|-----------|----------------|
| **Couche** | Business (interne) | API (externe) |
| **Usage** | Retour d'un tool exécuté | Réponse HTTP au client |
| **Contenu** | Success, Data, Error, Metadata | Success, Data, Message, StatusCode, Errors |
| **HTTP Status** | Non concerné | 200, 201, 400, 404, 500... |
| **Client** | Services internes | Frontend/Postman |

### Pattern de propagation

```
Tool (Business) 
  → ToolResult (internal)
    → ToolService (wraps in ApiResponse)
      → ChatService
        → ChatController (unwraps to IActionResult)
```

### Exemple ToolResult enrichi

```csharp
public class ToolResult
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
    
    // Execution metadata
    public string? ToolName { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime ExecutedAt { get; set; }
    public int? RetryCount { get; set; }
    
    // Source tracking
    public string? Source { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
```

---

## Structure des Fichiers

### Core - DTOs

```
Orion.Core/
├── DTOs/
│   ├── Internal/           ← DTOs Business uniquement
│   │   ├── LLM/
│   │   │   ├── OllamaResponse.cs
│   │   │   └── LLMToolCall.cs
│   │   └── Tools/
│   │       └── ToolInvocationContext.cs
│   ├── Requests/           ← Entrées API
│   │   ├── ChatRequest.cs
│   │   └── LLMRequest.cs
│   └── Responses/          ← Sorties API
│       ├── ApiResponse.cs
│       └── ChatResponse.cs
```

### Business - Services & Agents

```
Orion.Business/
├── Services/              ← Interface avec API
│   ├── ChatService.cs
│   ├── LLMService.cs
│   ├── MemoryService.cs
│   ├── MemoryConsolidator.cs
│   └── AuditService.cs
├── Agents/                ← Logique interne
│   ├── AgentLoop.cs       ← SEUL à parler au modèle
│   ├── ConversationAgent.cs
│   └── BriefingAgent.cs
├── Tools/                 ← ITool + ToolInvoker (exécute | diffère | REFUSE)
└── LLM/
    ├── LLMCascade.cs      ← NIM d'abord, Ollama en repli
    ├── NimAgentClient.cs
    ├── OllamaAgentClient.cs
    └── PromptBuilder.cs
```

### API - Controllers

```
Orion.Api/
├── Controllers/           ← Uniquement Services ici
│   ├── ChatController.cs  (IChatService)
│   └── HealthController.cs (ILLMService)
├── WebSockets/
│   └── VoiceWebSocketHandler.cs   ← /ws/voice full-duplex
├── Services/              ← BackgroundService (briefing, consolidation)
└── Middleware/
    └── ErrorHandlingMiddleware.cs
```

---

## Règles de Code

1. **Controllers** → injectent uniquement des **Services**
2. **Services** → orchestrent les **Agents** et **Repositories**
3. **Agents** → logique métier spécifique (LLM, mémoire, tools)
4. **Clients LLM** → communiquent avec NIM puis Ollama, en cascade
5. **Repositories** → accès données via EF Core

### Anti-patterns à éviter

❌ Controller → Agent (toujours passer par un Service)
❌ Service → Repository direct (utiliser UnitOfWork)
❌ Classes privées dans les fichiers (utiliser DTOs dans Core/Internal)
