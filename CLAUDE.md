# ORION — Personal AI Agent

## Project Overview
ORION est l'assistant IA personnel de Yawo Zoglo, standalone pour l'instant, prévu comme module HexaNexus.
Stack : **React 19 + Vite** (PWA) frontend, **.NET 9** backend, **Ollama** (LLM local) avec fallback **Claude API**, **Supabase + pgvector** (mémoire persistante), **ORION Daemon** (agent système Windows).
Langue de l'interface et des réponses : **Français**.

## Vision
```
ORION n'est pas un chatbot.
ORION est un agent qui :
  - Comprend le contexte long-terme (mémoire persistante)
  - Agit sur des systèmes réels (tools)
  - Contrôle la machine locale (daemon)
  - Est proactif (morning briefing, alertes)
  - S'intègre plus tard dans HexaNexus
```

## Architecture Globale
```
orion/
├── CLAUDE.md                  # CE FICHIER — point d'entrée agent
├── AGENTS.md                  # Instructions agents, tools, workflows
├── backend/                   # .NET 9 Web API — cerveau ORION
│   ├── Orion.Api/             # Controllers, Program.cs, Middleware
│   ├── Orion.Business/        # Services, AgentRunner, ToolExecutor
│   ├── Orion.Core/            # Entities, DTOs, Interfaces, Enums
│   └── Orion.Data/            # Supabase repos, VectorStore, Memory
├── frontend/                  # React 19 + Vite — PWA installable
│   └── src/
│       ├── components/        # UI : Chat, VoiceButton, StatusBar
│       ├── pages/             # Home, Settings, Memory, Briefing
│       ├── services/          # API clients (chat, tools, memory)
│       ├── hooks/             # useChat, useVoice, useStream
│       ├── types/             # Interfaces TS : Message, Tool, Memory
│       └── config/            # endpoints, env vars
├── daemon/                    # .NET 9 Console — agent système Windows
│   ├── Orion.Daemon/          # Service Windows, WebSocket listener
│   ├── actions/               # OpenApp, RunScript, LaunchEditor, etc.
│   └── daemon.config.json     # Apps autorisées, chemins, permissions
├── memory/                    # Schémas et scripts mémoire
│   ├── schema.sql             # Tables Supabase (conversations, vectors)
│   ├── seed.sql               # Données initiales (profil Yawo)
│   └── README.md              # Comment fonctionne la mémoire ORION
├── tools/                     # Définitions des tools (contrats)
│   ├── definitions/           # JSON Schema de chaque tool
│   └── README.md              # Comment créer un nouveau tool
├── docker-compose.yml         # Ollama + API pour dev local
└── .env.example               # Variables d'environnement requises
```

## Stack Technique Complète

| Couche | Technologie | Rôle |
|---|---|---|
| Frontend | React 19 + Vite + TypeScript + TailwindCSS | PWA installable mobile + desktop |
| Backend | .NET 9, ASP.NET Core | API REST + WebSocket streaming |
| LLM principal | Ollama (Kimi K2 / Qwen2.5) — local Windows | Inférence offline, gratuit |
| LLM fallback | Claude API (claude-sonnet-4-20250514) | Quand Ollama injoignable (mobile) |
| Mémoire court terme | RAM (ConversationManager) | Contexte session en cours |
| Mémoire long terme | Supabase PostgreSQL + pgvector | RAG, profil, historique |
| Daemon système | .NET 9 Console Service Windows | Actions OS : apps, scripts, fichiers |
| Déploiement backend | Render (free tier, Docker) | API accessible partout |
| Déploiement frontend | Vercel | PWA HTTPS obligatoire |
| CI/CD | GitHub Actions | Build + deploy automatique |

## LLM — Stratégie Dual-Mode

```
Request reçue
     │
     ▼
Ollama joignable ? (http://localhost:11434)
     │
     ├── OUI → Ollama local (Kimi K2 ou Qwen2.5-coder)
     │           Gratuit, rapide, offline, privacy totale
     │
     └── NON → Fallback Claude API (Anthropic)
                Payant, cloud, actif quand en déplacement
```

### Modèles Ollama recommandés
```bash
ollama pull kimi-k2          # Raisonnement + tool use
ollama pull qwen2.5-coder    # Si tâches code lourdes
```

### Interface abstraite (immuable)
```csharp
// Orion.Core/Interfaces/ILLMClient.cs
public interface ILLMClient
{
    Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken ct = default);
    Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default);
    bool IsAvailable();
}
// Implémentations : OllamaClient.cs, AnthropicClient.cs
// Sélection : LLMRouter.cs (vérifie Ollama, fallback Claude)
```

## Mémoire — Architecture

```
Mémoire court terme (RAM)        Mémoire long terme (Supabase)
─────────────────────────        ──────────────────────────────
Session en cours                 Persiste entre sessions
Messages de la conv              Profil utilisateur
Contexte outils actifs           Historique résumé
Vidée à chaque session           Embeddings pgvector (RAG)
~20 derniers messages max        Croissance illimitée
```

### Schéma Supabase (voir memory/schema.sql)
```sql
-- 4 tables principales
conversations      -- sessions de chat
messages           -- messages individuels
memory_vectors     -- embeddings pgvector (RAG)
user_profile       -- profil Yawo (projets, préférences, contexte)
```

### Flux RAG à chaque requête
```
Message reçu
     │
     ▼
Génère embedding du message (Ollama nomic-embed-text)
     │
     ▼
Recherche top-5 souvenirs pertinents (pgvector cosine similarity)
     │
     ▼
Injecte dans le contexte LLM : [profil] + [souvenirs] + [conv courante]
     │
     ▼
LLM répond avec contexte enrichi
     │
     ▼
Sauvegarde message + embedding en DB
```

## Diagrammes d'Architecture

### Diagramme de Classes — Backend Core
```
┌─────────────────────────────────────────────────────────────────┐
│                        ORION.CORE                               │
│                                                                 │
│  <<interface>>          <<interface>>        <<interface>>       │
│  ILLMClient             ITool                IDaemonClient       │
│  ─────────────          ─────────────        ──────────────      │
│  +CompleteAsync()       +Name: string        +SendActionAsync()  │
│  +StreamAsync()         +Description         +IsConnected: bool  │
│  +IsAvailable()         +InputSchema                            │
│  +Provider              +ExecuteAsync()                         │
│       ▲                      ▲                     ▲            │
└───────┼──────────────────────┼─────────────────────┼───────────┘
        │                      │                     │
┌───────┼──────────────────────┼─────────────────────┼───────────┐
│       │         ORION.BUSINESS                     │           │
│                                                                 │
│  OllamaClient      GetShiftStarStatsTool    DaemonWsClient      │
│  AnthropicClient   GetShiftStarVotesTool                        │
│       │            MorningBriefingTool                          │
│       │            OpenAppTool (→ Daemon)                       │
│       │                                                         │
│  LLMRouter ──────────────────────────────────────────────────   │
│  (sélectionne Ollama ou Anthropic selon disponibilité)          │
│                                                                 │
│  ToolRegistry ──── [ ITool, ITool, ITool, ... ]                 │
│  (registre de tous les tools disponibles)                       │
│                                                                 │
│  ConversationAgent ◄──── MemoryAgent                            │
│         │                  (RAG = récupération des souvenirs)   │
│         └──────────────► ToolAgent                              │
│                           BriefingAgent (cron 07h00)            │
└─────────────────────────────────────────────────────────────────┘
```

### Diagramme de Données — Supabase
```
┌──────────────────┐         ┌──────────────────────┐
│  conversations   │         │  user_profile         │
│  ──────────────  │         │  ────────────────     │
│  id (UUID) PK    │         │  key (TEXT) PK         │
│  type            │         │  value (TEXT)          │
│  started_at      │         │  updated_at            │
│  ended_at        │         └──────────────────────┘
│  llm_provider    │
│  summary         │         ┌──────────────────────┐
└────────┬─────────┘         │  memory_vectors       │
         │ 1                 │  ────────────────     │
         │                   │  id (UUID) PK         │
         │ N                 │  content (TEXT)        │
┌────────▼─────────┐         │  embedding vector(768)│
│  messages        │         │  source                │
│  ──────────────  │         │  importance (FLOAT)    │
│  id (UUID) PK    │         │  created_at            │
│  conversation_id │◄──────  │  last_accessed         │
│  role            │  1      └──────────────────────┘
│  content         │
│  tool_name       │         ┌──────────────────────┐
│  tool_input JSON │         │  tool_executions      │
│  tool_result JSON│         │  ────────────────     │
│  created_at      │         │  id (UUID) PK         │
└────────┬─────────┘         │  message_id (FK)      │
         │ 1                 │  tool_name             │
         │                   │  input JSONB           │
         │ N                 │  result JSONB          │
┌────────▼─────────┐         │  status                │
│  tool_executions │         │  duration_ms           │
│  (voir ci-contre)│         │  executed_at           │
└──────────────────┘         └──────────────────────┘
```

### Diagramme de Flux — Requête complète
```
[User PWA] ──POST /chat──► [ChatController]
                                  │
                                  ▼
                        [ConversationAgent]
                           │         │
                    [MemoryAgent]    │
                    1. Load profil   │
                    2. Embed msg     │
                    3. pgvector top5 │
                    4. Return ctx    │
                           │         │
                           ▼         │
                    [LLMRouter]      │
                    Ollama dispo ?   │
                    OUI → Ollama     │
                    NON → Claude API │
                           │         │
                    LLM répond       │
                    tool_call ?      │
                    OUI ─────────────┘
                           │
                    [ToolAgent]
                    Système tool ?
                    OUI → [DaemonWsClient] → [Daemon Windows]
                    NON → Exécute directement (Supabase, API)
                           │
                    [ConversationAgent]
                    Sauvegarde message + embedding
                           │
                    SSE stream ──► [Frontend StreamingText.tsx]
```

### Diagramme Voix — Niveaux d'implémentation
```
NIVEAU 1 (Phase 1) — Web Speech API, 0 coût
─────────────────────────────────────────────
Micro → [SpeechRecognition API] → texte → backend
texte ← [SpeechSynthesis API] ← réponse ORION
Qualité : correcte | Latence : 1-2s | Coût : 0€

NIVEAU 2 (Phase 4) — Whisper + ElevenLabs
──────────────────────────────────────────
Micro → [MediaRecorder] → fichier audio
      → [Whisper API] → transcription précise
      → backend → réponse texte
      → [ElevenLabs API] → audio naturel → lecture
Qualité : excellente | Latence : 2-4s | Coût : ~€0.01/min

NIVEAU 3 (Phase 5, optionnel) — Temps réel
───────────────────────────────────────────
Micro → [VAD (Voice Activity Detection)] → détecte fin de phrase
      → WebRTC → streaming audio backend
      → STT streaming → LLM streaming → TTS streaming → audio
Qualité : iron man | Latence : <1s | Coût : ~€0.05/min
```

## Tools — Comment ça fonctionne

### Principe
Le LLM décide d'appeler un tool via function calling.
Le backend exécute le tool réel et renvoie le résultat au LLM.
Le LLM formule la réponse finale.

```
User: "Combien d'utilisateurs actifs sur ShiftStar ?"
     │
     ▼
LLM → tool_call: get_shiftstar_stats({ metric: "active_users" })
     │
     ▼
Backend → Supabase ShiftStar (service role key)
     │
     ▼
Résultat → LLM → "Il y a 40 utilisateurs actifs sur ShiftStar."
```

### Tools Phase 1 (MVP — ShiftStar + Briefing)
```
get_shiftstar_stats        Stats générales : users actifs, votes, MRR
get_shiftstar_votes        Votes récents par établissement
get_shiftstar_mrr          MRR actuel + évolution mensuelle
get_shiftstar_tenants      Liste établissements actifs + statut abonnement
create_shiftstar_challenge Crée un challenge depuis ORION
morning_briefing           Agrège tout + résume la journée
send_notification          Envoie une notification push PWA
```

### Tools Phase 2 (Système — via Daemon Windows)
```
open_app                   Ouvre une application (whitelist)
open_file_in_editor        Ouvre un fichier précis dans VS Code
run_script                 Exécute un script PowerShell
launch_claude              Ouvre Claude dans le navigateur
open_browser_url           Ouvre une URL dans le navigateur par défaut
get_system_status          CPU, RAM, disque, processus actifs
read_file                  Lit le contenu d'un fichier local
write_file                 Modifie/crée un fichier local
git_status                 Statut git d'un repo (branche, fichiers modifiés)
git_commit                 Commit rapide avec message depuis ORION
```

### Tools Phase 3 (Connecteurs externes)
```
get_emails                 Gmail API — emails non lus + résumé
send_email                 Gmail API — envoie un email
get_calendar               Google Calendar — événements du jour/semaine
web_search                 Recherche web (SerpAPI ou DuckDuckGo)
check_render_deploy        Statut et logs déploiement Render
check_vercel_deploy        Statut et logs déploiement Vercel
get_supabase_logs          Logs d'erreur Supabase (ShiftStar, ORION)
send_whatsapp              WhatsApp Business API — message rapide
linkedin_draft             Prépare un post LinkedIn (texte, hashtags)
```

### Créer un nouveau tool (procédure)
1. Définir le contrat JSON dans `tools/definitions/{tool_name}.json`
2. Implémenter `ITool` dans `Orion.Business/Tools/{ToolName}Tool.cs`
3. Enregistrer dans `ToolRegistry.cs`
4. Si action système → implémenter aussi dans `daemon/actions/`
5. Documenter dans `tools/README.md`

## Structure Backend Détaillée — 4 Couches

### Règle fondamentale
```
Core        → ne dépend de rien
Business    → dépend de Core uniquement
Data        → dépend de Core uniquement
Api         → dépend de Business uniquement

Interfaces  → définies dans Core
Implémentations → dans Business (services/agents) ou Data (repositories)
Jamais l'inverse.
```

### Orion.Core
```
Orion.Core/
├── Entities/
│   ├── Conversation.cs
│   ├── Message.cs
│   ├── MemoryVector.cs
│   └── UserProfile.cs
│
├── DTOs/
│   ├── Requests/
│   │   ├── ChatRequest.cs
│   │   ├── VoiceRequest.cs
│   │   └── MemorySearchRequest.cs
│   └── Responses/
│       ├── ApiResponse.cs         # Pattern ShadowCat — retourné par tous les controllers
│       ├── ChatResponse.cs
│       ├── BriefingDto.cs
│       └── ToolCallDto.cs
│
├── Interfaces/
│   ├── Repositories/
│   │   ├── IGenericRepository.cs  # Pattern ShadowCat — CRUD générique + pagination
│   │   ├── IConversationRepository.cs
│   │   ├── IMessageRepository.cs
│   │   ├── IMemoryRepository.cs   # + SearchSimilarAsync() pour pgvector
│   │   └── IUserProfileRepository.cs
│   ├── Agents/
│   │   ├── IConversationAgent.cs
│   │   ├── IMemoryAgent.cs
│   │   ├── IToolAgent.cs
│   │   └── IBriefingAgent.cs
│   ├── LLM/
│   │   ├── ILLMClient.cs          # IMMUABLE
│   │   └── ILLMRouter.cs
│   ├── Services/
│   │   ├── IEmbeddingService.cs
│   │   └── IPushNotificationService.cs
│   ├── Tools/
│   │   ├── ITool.cs
│   │   └── IToolRegistry.cs
│   └── Daemon/
│       └── IDaemonClient.cs
│
├── Common/
│   └── Result.cs                  # Result<T> interne (entre couches Business/Data)
│
└── Configuration/
    ├── OllamaOptions.cs
    ├── AnthropicOptions.cs
    └── DaemonOptions.cs
```

### Contrats Core — extraits clés

```csharp
// IGenericRepository.cs — adapté de ShadowCat, identique en structure
// Toutes les méthodes retournent T?, IEnumerable<T>, bool, int
// Les retours ApiResponse<T> sont gérés au niveau Controller uniquement

public interface IGenericRepository<T, TId> where T : class
{
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);
    Task<T?> GetByIdAsync(TId id, CancellationToken ct = default);
    Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);
    Task<(IEnumerable<T> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize,
        Expression<Func<T, bool>>? filter = null,
        Expression<Func<T, object>>? orderBy = null,
        bool ascending = true, CancellationToken ct = default);
    void Update(T entity);
    void UpdateRange(IEnumerable<T> entities);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<T?> GetWithIncludesAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default,
        params Expression<Func<T, object>>[] includes);
}

// IMemoryRepository.cs — étend IGenericRepository avec pgvector
public interface IMemoryRepository : IGenericRepository<MemoryVector, Guid>
{
    // Recherche sémantique par similarité cosinus (pgvector)
    Task<IEnumerable<MemoryVector>> SearchSimilarAsync(
        float[] embedding, int topK = 5, CancellationToken ct = default);
    Task<IEnumerable<MemoryVector>> GetBySourceAsync(
        string source, CancellationToken ct = default);
}

// IUnitOfWork.cs — pattern ShadowCat, adapté ORION
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    IConversationRepository Conversations { get; }
    IMessageRepository Messages { get; }
    IMemoryRepository Memory { get; }
    IUserProfileRepository UserProfile { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct = default);
}

// ILLMClient.cs — IMMUABLE
public interface ILLMClient
{
    Task<ApiResponse<LLMResponse>> CompleteAsync(LLMRequest request, CancellationToken ct = default);
    Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default);
    bool IsAvailable();
    LLMProvider Provider { get; }
}

// ITool.cs
public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonObject InputSchema { get; }
    Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default);
}
```

### Règle de retour par couche — IMMUABLE
```
Couche          Retourne              Raison
──────────────────────────────────────────────────────────────
Data            T? / IEnumerable<T>  Repositories : données brutes
Business        ApiResponse<T>       Connaît le sens métier de l'erreur
                                     Décide si c'est 404, 422, 401, 500
Controller      IActionResult        Unwrap ApiResponse → HTTP status code
                                     Aucune logique métier ici
```

```csharp
// Business — ConversationAgent.cs
public async Task<ApiResponse<ChatResponse>> ProcessAsync(ChatRequest request, CancellationToken ct)
{
    var conversation = await _uow.Conversations.GetByIdAsync(request.SessionId, ct);
    if (conversation is null)
        return ApiResponse<ChatResponse>.NotFoundResponse("Session introuvable");

    var llmResult = await _llmRouter.CompleteAsync(prompt, ct);
    if (!llmResult.Success)
        return ApiResponse<ChatResponse>.ErrorResponse("LLM indisponible", 503);

    return ApiResponse<ChatResponse>.SuccessResponse(new ChatResponse(llmResult.Data));
}

// Controller — ChatController.cs
// Unwrap uniquement — aucune logique métier
[HttpPost("chat")]
public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken ct)
{
    var response = await _conversationAgent.ProcessAsync(request, ct);
    return StatusCode(response.StatusCode, response);
}
```

### ApiResponse<T> — factory methods
```csharp
ApiResponse<T>.SuccessResponse(data)              // 200
ApiResponse<T>.CreatedResponse(data)              // 201
ApiResponse<T>.ErrorResponse(message, code)       // 4xx/5xx
ApiResponse<T>.NotFoundResponse(message)          // 404
ApiResponse<T>.UnauthorizedResponse(message)      // 401
ApiResponse<T>.ForbiddenResponse(message)         // 403
ApiResponse<T>.ValidationErrorResponse(dict)      // 422
```

### Orion.Business
```
Orion.Business/
├── Agents/
│   ├── ConversationAgent.cs      # IConversationAgent
│   ├── MemoryAgent.cs            # IMemoryAgent
│   ├── ToolAgent.cs              # IToolAgent
│   └── BriefingAgent.cs         # IBriefingAgent + IHostedService (cron)
├── LLM/
│   ├── LLMRouter.cs              # ILLMRouter — ping Ollama, fallback Claude
│   ├── OllamaClient.cs           # ILLMClient
│   ├── AnthropicClient.cs        # ILLMClient
│   └── PromptBuilder.cs          # Construit les prompts système ORION
├── Tools/
│   ├── ToolRegistry.cs           # IToolRegistry
│   ├── GetShiftStarStatsTool.cs  # ITool — retourne ApiResponse<ToolResult>
│   ├── GetShiftStarVotesTool.cs
│   ├── GetShiftStarMrrTool.cs
│   ├── GetShiftStarTenantsTool.cs
│   ├── CreateChallengeTool.cs
│   ├── MorningBriefingTool.cs
│   ├── SendNotificationTool.cs
│   └── OpenAppTool.cs            # → délègue au Daemon
├── Daemon/
│   ├── DaemonWebSocketClient.cs  # IDaemonClient
│   └── DaemonActionValidator.cs  # Vérifie whitelist avant envoi
└── Services/
    ├── EmbeddingService.cs       # IEmbeddingService — Ollama nomic-embed-text
    └── PushNotificationService.cs # IPushNotificationService — Web Push API
```

### Orion.Data
```
Orion.Data/
├── Repositories/
│   ├── GenericRepository.cs          # Implémente IGenericRepository<T, TId>
│   ├── ConversationRepository.cs     # : GenericRepository<Conversation, Guid>
│   ├── MessageRepository.cs          # : GenericRepository<Message, Guid>
│   ├── MemoryRepository.cs           # : GenericRepository<MemoryVector, Guid>
│   │                                 #   + SearchSimilarAsync (pgvector SQL)
│   └── UserProfileRepository.cs      # : GenericRepository<UserProfile, string>
├── UnitOfWork/
│   └── UnitOfWork.cs                 # Implémente IUnitOfWork
├── Context/
│   └── SupabaseContext.cs            # Client Supabase configuré
└── Mappings/
    └── SupabaseMappings.cs           # Mapping entités ↔ tables Supabase
```

### Orion.Api
```
Orion.Api/
├── Controllers/
│   ├── ChatController.cs         # POST /chat → ApiResponse<ChatResponse>
│   ├── MemoryController.cs       # GET /memory → ApiResponse<List<MemoryVector>>
│   ├── ToolsController.cs        # GET /tools → ApiResponse<List<ToolInfo>>
│   ├── BriefingController.cs     # GET /briefing/today → ApiResponse<BriefingDto>
│   └── HealthController.cs       # GET /health (Render health check, pas d'ApiResponse)
├── Middleware/
│   ├── AuthMiddleware.cs
│   └── ErrorHandlingMiddleware.cs # Catch global → ApiResponse<object>.ErrorResponse
├── Program.cs                    # DI, CORS, middleware, SignalR
└── appsettings.json
```

## ORION Daemon — Agent Système Windows

### Rôle
Programme .NET 9 installé comme **service Windows** (toujours actif, même sans session utilisateur ouverte).
Il écoute les commandes du backend Orion via WebSocket sécurisé.

### Sécurité
```
- Whitelist stricte des apps autorisées (daemon.config.json)
- Token partagé backend ↔ daemon (jamais exposé au frontend)
- Aucune commande arbitraire — uniquement actions prédéfinies
- Logs de chaque action exécutée
```

### Communication
```
Backend (Render)
     │
     │  WebSocket WSS (token auth)
     ▼
Daemon (Windows Service local)
     │
     ├── Reçoit : { action: "open_app", payload: { app: "vscode" } }
     ├── Vérifie whitelist
     ├── Exécute Process.Start(...)
     └── Répond : { success: true, pid: 1234 }
```

### Installation daemon
```powershell
# Dans daemon/ après build
sc create OrionDaemon binPath="C:\orion\daemon\Orion.Daemon.exe"
sc start OrionDaemon
# Ou via : dotnet publish + Windows Task Scheduler
```

## Frontend — PWA

### Pourquoi React + Vite (et pas Next.js)
- Même stack que ShiftStar : zéro courbe d'apprentissage
- PWA pure : pas besoin de SSR pour un assistant personnel
- Vite = build ultra-rapide, HMR (rechargement instantané en dev)
- Next.js apporterait du SSR (rendu côté serveur) inutile ici

### Fonctionnalités PWA
```
- Installable sur Android (Add to Home Screen)
- Installable sur Windows (navigateur → installer l'app)
- Service Worker : cache assets offline, réception push
- Push Notifications : morning briefing, alertes ORION (app fermée)
- Web Speech API : STT/TTS natif Phase 2 → WebRTC Phase 4
```

### Structure Frontend Complète
```
frontend/
├── public/
│   ├── manifest.json              # Config PWA : nom, icônes, couleurs, display
│   ├── sw.js                      # Service Worker : cache + push notifications
│   └── icons/                     # Icônes PWA 192x192, 512x512
│
└── src/
    ├── algorithms/
    │   └── vadProcessor.ts        # Voice Activity Detection (Phase 4)
    │                              # Détecte automatiquement fin de parole
    │
    ├── assets/
    │   └── orion-logo.svg
    │
    ├── components/
    │   ├── chat/
    │   │   ├── ChatWindow.tsx     # Conteneur principal conversation
    │   │   ├── MessageBubble.tsx  # Bulle message user / ORION (markdown rendu)
    │   │   ├── StreamingText.tsx  # Texte ORION qui s'affiche mot par mot (SSE)
    │   │   ├── ToolCallCard.tsx   # Affiche : tool appelé + input + résultat
    │   │   └── ChatInput.tsx      # Zone de saisie + bouton envoi + bouton micro
    │   ├── memory/
    │   │   ├── MemoryPanel.tsx    # Liste des souvenirs récents
    │   │   └── MemoryCard.tsx     # Souvenir individuel : contenu + date + importance
    │   ├── briefing/
    │   │   ├── BriefingCard.tsx   # Morning briefing formaté avec sections
    │   │   └── BriefingItem.tsx   # Item individuel du briefing
    │   ├── voice/
    │   │   ├── VoiceButton.tsx    # Bouton micro : idle / recording / processing
    │   │   └── VoiceVisualizer.tsx # Onde sonore animée pendant enregistrement
    │   └── ui/
    │       ├── StatusBar.tsx      # Mode LLM actif (Ollama/Claude), daemon connecté
    │       ├── Spinner.tsx
    │       ├── Badge.tsx          # Ex: badge "Ollama" ou "Claude"
    │       └── MarkdownRenderer.tsx # Rendu markdown dans les messages ORION
    │
    ├── config/
    │   └── endpoints.ts           # Toutes les URLs API centralisées
    │                              # VITE_API_URL + routes typées
    │
    ├── context/
    │   ├── ChatContext.tsx        # État global conversation (messages, session)
    │   ├── OrionStatusContext.tsx # État connexion : Ollama dispo, daemon connecté
    │   └── NotifContext.tsx       # État permissions push notifications
    │
    ├── hooks/
    │   ├── useChat.ts             # Envoie message, reçoit stream SSE, historique
    │   ├── useStream.ts           # Lecture SSE (Server-Sent Events = stream texte)
    │   │                         # SSE : flux unidirectionnel serveur → client
    │   ├── useVoice.ts            # getUserMedia, MediaRecorder, envoi audio
    │   ├── useVAD.ts              # Intègre vadProcessor — détecte fin de parole
    │   ├── usePushNotif.ts        # Service Worker + permission notifications
    │   └── useOrionStatus.ts      # Ping backend : mode LLM actif, daemon up/down
    │
    ├── pages/
    │   ├── Home.tsx               # Chat principal — vue par défaut
    │   ├── Memory.tsx             # Visualisation + suppression souvenirs
    │   ├── Briefing.tsx           # Historique des morning briefings
    │   └── Settings.tsx           # Config : LLM préféré, heure briefing, notifs
    │
    ├── services/
    │   ├── chatService.ts         # POST /chat, GET /chat/stream (SSE)
    │   │                         # Retourne ApiResponse<ChatResponse>
    │   ├── memoryService.ts       # GET /memory, DELETE /memory/:id
    │   │                         # Retourne ApiResponse<MemoryVector[]>
    │   ├── toolsService.ts        # GET /tools
    │   │                         # Retourne ApiResponse<ToolInfo[]>
    │   ├── briefingService.ts     # GET /briefing/today, POST /briefing/trigger
    │   │                         # Retourne ApiResponse<BriefingDto>
    │   └── voiceService.ts        # POST /voice (audio → texte → réponse audio)
    │                              # Retourne ApiResponse<VoiceResponse>
    │
    ├── types/
    │   ├── api/
    │   │   └── apiResponse.ts     # Interface TypeScript miroir de ApiResponse<T> .NET
    │   ├── dto/
    │   │   ├── chatDto.ts         # ChatRequest, ChatResponse
    │   │   ├── memoryDto.ts       # MemoryVector, MemorySearchRequest
    │   │   ├── briefingDto.ts     # BriefingDto, BriefingItem
    │   │   ├── toolDto.ts         # ToolInfo, ToolCallDto, ToolResult
    │   │   └── voiceDto.ts        # VoiceRequest, VoiceResponse
    │   └── models/
    │       ├── message.ts         # Message, Role ('user' | 'assistant' | 'tool')
    │       ├── conversation.ts    # Conversation, SessionState
    │       └── orionStatus.ts     # LLMProvider, DaemonStatus, ConnectionState
    │
    ├── utils/
    │   ├── markdownUtils.ts       # Helpers rendu markdown
    │   ├── audioUtils.ts          # Conversion formats audio, base64
    │   └── dateUtils.ts           # Formatage dates FR
    │
    ├── App.tsx                    # Router principal (React Router v6)
    ├── main.tsx                   # Entry point + enregistrement Service Worker
    ├── index.css                  # Tailwind imports
    └── vite-env.d.ts
    
# Fichiers racine
├── index.html
├── .env                           # VITE_API_URL, VITE_WS_URL (gitignored)
├── .env.example
├── package.json
├── tailwind.config.js
├── tsconfig.json
├── tsconfig.node.json
├── postcss.config.js
└── vite.config.ts                 # PWA plugin (vite-plugin-pwa)
```

### Types Frontend — miroir exact du backend

```typescript
// types/api/apiResponse.ts — miroir de ApiResponse<T> .NET
export interface ApiResponse<T> {
  success: boolean
  message: string
  data: T | null
  errors: string[]
  validationErrors: Record<string, string[]>
  statusCode: number
  timestamp: string
  traceId: string
}

// types/models/message.ts
export type Role = 'user' | 'assistant' | 'tool'
export interface Message {
  id: string
  role: Role
  content: string
  toolName?: string
  toolInput?: Record<string, unknown>
  toolResult?: Record<string, unknown>
  createdAt: string
}

// types/models/orionStatus.ts
export type LLMProvider = 'ollama' | 'anthropic'
export type DaemonStatus = 'connected' | 'disconnected' | 'unknown'
export interface OrionStatus {
  llmProvider: LLMProvider
  ollamaAvailable: boolean
  daemonConnected: DaemonStatus
  backendLatencyMs: number
}

// types/dto/chatDto.ts
export interface ChatRequest {
  message: string
  sessionId: string
}
export interface ChatResponse {
  response: string
  toolsCalled: ToolCallDto[]
  llmProvider: LLMProvider
  memoryUsed: number   // nombre de souvenirs injectés
}
```

### Règle services frontend
```typescript
// Chaque service retourne ApiResponse<T> tel quel — pas de wrapping
// La gestion d'erreur se fait dans les hooks, pas dans les services

// chatService.ts
export const sendMessage = async (req: ChatRequest): Promise<ApiResponse<ChatResponse>> => {
  const res = await fetch(endpoints.chat, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req)
  })
  return res.json()
}

// useChat.ts — gère success/error
const { data, errors } = await sendMessage(req)
if (!data) { setError(errors[0]); return }
setMessages(prev => [...prev, buildMessage(data.response)])
```

## Déploiement

### Backend (Render)
```
Service : Web Service
Runtime : Docker
Dockerfile : ./backend/Dockerfile
Health check : GET /health
Variables d'environnement :
  SUPABASE_URL=
  SUPABASE_SERVICE_KEY=
  ANTHROPIC_API_KEY=        # Fallback Claude
  OLLAMA_URL=               # http://localhost:11434 (ou tunnel)
  DAEMON_WS_TOKEN=          # Token auth daemon
  JWT_SECRET=
```

### Frontend (Vercel)
```
Framework : Vite
Build command : npm run build
Output : dist/
Variables :
  VITE_API_URL=https://orion-api.onrender.com
  VITE_WS_URL=wss://orion-api.onrender.com
```

### Ollama (Windows local)
```powershell
# Ollama tourne comme service Windows
# Accessible sur http://localhost:11434
# Exposé via Cloudflare Tunnel si besoin (non recommandé par défaut)
ollama serve
```

## Dev Local
```bash
# 1. Backend
cd backend
dotnet run --project Orion.Api
# API : http://localhost:5000 | Swagger : http://localhost:5000/swagger

# 2. Frontend
cd frontend
npm run dev
# http://localhost:5173

# 3. Daemon (PowerShell admin)
cd daemon
dotnet run --project Orion.Daemon
# WebSocket : ws://localhost:5001

# 4. Ollama (déjà installé Windows)
ollama serve
# http://localhost:11434
```

## Variables d'environnement (.env.example)
```env
# Backend
SUPABASE_URL=https://xxx.supabase.co
SUPABASE_SERVICE_KEY=eyJ...
ANTHROPIC_API_KEY=sk-ant-...
OLLAMA_BASE_URL=http://localhost:11434
OLLAMA_MODEL=kimi-k2
DAEMON_WS_URL=ws://localhost:5001
DAEMON_WS_TOKEN=secret-token-orion
JWT_SECRET=orion-jwt-secret-change-this

# Frontend
VITE_API_URL=http://localhost:5000
VITE_WS_URL=ws://localhost:5000
```

## Roadmap

### Phase 1 — Core MVP (2-3 semaines)
- [ ] Backend : ConversationManager + LLMRouter (Ollama/Claude)
- [ ] Backend : Mémoire Supabase + pgvector RAG
- [ ] Backend : Tools ShiftStar (stats, votes)
- [ ] Frontend : Chat PWA installable
- [ ] Frontend : Streaming réponses temps réel
- [ ] Morning briefing automatique (BackgroundService .NET)

### Phase 2 — Daemon système (1-2 semaines)
- [ ] Daemon Windows Service
- [ ] Tools : open_app, run_script, open_editor, launch_claude
- [ ] WebSocket sécurisé backend ↔ daemon
- [ ] Whitelist apps configurables

### Phase 3 — Connecteurs (continu)
- [ ] Gmail API
- [ ] Google Calendar
- [ ] Vercel API
- [ ] Web search

### Phase 4 — Voix temps réel (après Phase 2)
# Stack WebRTC déjà maîtrisé via ShadowCat (DataChannel → MediaStream, delta minimal)
- [ ] getUserMedia() → MediaRecorder → stream audio vers backend
- [ ] VAD : @ricky0123/vad-web (détecte fin de parole automatiquement — évite bouton)
- [ ] Backend : Whisper.net (NuGet) — STT (Speech-to-Text = audio → texte)
- [ ] Backend : Kokoro-net — TTS local gratuit (Text-to-Speech = texte → voix naturelle)
- [ ] AudioContext frontend — lecture audio retour ORION
- [ ] WebRTC signaling réutilisé de ShadowCat (ICE candidates, peer connection identiques)

### Phase 5 — HexaNexus
- [ ] Widget ORION dans HexaNexus dashboard
- [ ] Contexte partagé (même Supabase)
- [ ] Auth unifiée

## Règles de développement
- **Repository Pattern obligatoire** sur toute la couche Data
- **Strict TypeScript** frontend — no any
- **Interface ILLMClient** : ne jamais appeler Ollama ou Claude directement sans passer par le router
- **Tout tool** doit implémenter `ITool` et être enregistré dans `ToolRegistry`
- **Daemon** : toute nouvelle action doit être dans la whitelist avant d'être implémentable
- **Mémoire** : toute conversation DOIT être persistée — aucune exception
- **Logs** : chaque tool call, chaque action daemon, chaque fallback LLM est loggué