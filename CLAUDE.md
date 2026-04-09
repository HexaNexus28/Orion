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
├── backend/                   # .NET 9 Web API — cerveau ORION — déployé sur Render
│   ├── Orion.Api/             # Controllers, Program.cs, Middleware
│   │   ├── Controllers/       # Chat, Memory, Daemon, Voice, Briefing, Tools, Health
│   │   ├── Middleware/        # Auth, ErrorHandling, Logging, DaemonWebSocket
│   │   └── Program.cs         # DI, CORS, middleware pipeline
│   ├── Orion.Business/        # Agents, LLM, Tools, Services
│   │   ├── Agents/            # ConversationAgent, MemoryAgent, ToolAgent, BriefingAgent
│   │   ├── LLM/               # LLMRouter, OllamaClient, AnthropicClient
│   │   ├── Services/          # ChatService, MemoryService, WhisperService, BriefingService
│   │   └── Tools/             # Tool implementations + ToolRegistry
│   ├── Orion.Core/            # Entities, DTOs, Interfaces, Configuration
│   │   ├── DTOs/Requests/     # ChatRequest, VoiceRequest, MemorySearchRequest
│   │   ├── DTOs/Responses/    # ChatResponse, VoiceResponse, ApiResponse
│   │   ├── Entities/          # Conversation, Message, MemoryVector
│   │   └── Interfaces/        # Repositories, Services, Agents, LLM
│   └── Orion.Data/            # Repositories, UnitOfWork, Context, Mappings
├── frontend/                  # React 19 + Vite — PWA installable — déployé sur Vercel
│   └── src/
│       ├── components/        # UI : Entité, Input, Overlays, Canvas, 3D
│       ├── services/          # API clients — TOUS en axios + endpoints.ts
│       ├── hooks/             # useChat, useVoice, useStream, useGestures, useHandTracking
│       ├── types/             # Interfaces TS strictes — aucun 'any'
│       └── config/            # endpoints.ts (centralisé), env vars
├── daemon/                    # .NET 9 Worker Service — installé sur PC Windows
│   ├── Orion.Daemon/          # Programme principal — service Windows
│   ├── Orion.Daemon.Core/     # Entities, Interfaces, Configuration (propre au daemon)
│   └── Orion.Daemon.Actions/  # Implémentations IAction (OpenApp, RunScript...)
├── memory/                    # Schémas et scripts mémoire
│   ├── schema.sql             # Tables Supabase (conversations, vectors)
│   ├── seed.sql               # Données initiales (profil Yawo)
│   └── README.md              # Comment fonctionne la mémoire ORION
├── tools/                     # Définitions des tools (contrats)
│   ├── definitions/           # JSON Schema de chaque tool
│   └── README.md              # Comment créer un nouveau tool
├── docker-compose.yml         # Backend + Ollama pour dev local
└── .env.example               # Variables d'environnement requises
```

### Pourquoi pas de projet Orion.Shared
```
Le contrat entre backend et daemon = JSON sur WebSocket
Pas de référence de projet entre les deux solutions

Backend construit DaemonCommand en JSON → envoie via WSS
Daemon désérialise avec ses propres types → exécute → répond en JSON
Backend désérialise DaemonResponse avec ses propres types

Chaque côté définit ses types indépendamment.
Le JSON est le contrat — pas une DLL partagée.
```

## Stack Technique Complète

| Couche | Technologie | Rôle |
|---|---|---|
| Frontend | React 19 + Vite + TypeScript + TailwindCSS | PWA installable mobile + desktop |
| Backend | .NET 9, ASP.NET Core | API REST + WebSocket streaming |
| LLM principal | Ollama (Qwen2.5:14b) — local Windows | Inférence offline, gratuit |
| LLM fallback | Claude API (claude-sonnet-4-20250514) | Quand Ollama injoignable (mobile) |
| Mémoire court terme | RAM (ConversationManager) | Contexte session en cours |
| Mémoire long terme | Supabase PostgreSQL + pgvector | RAG, profil, historique |
| Daemon système | .NET 9 Worker Service Windows | Actions OS : apps, scripts, fichiers |
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
     ├── OUI → Ollama local (Qwen2.5:14b)
     │           Gratuit, rapide, offline, privacy totale
     │
     └── NON → Fallback Claude API (Anthropic)
                Payant, cloud, actif quand en déplacement
```

### Modèles Ollama recommandés
```bash
ollama pull qwen2.5:14b      # Principal — raisonnement + tool use — ~9GB (GPU 8GB+)
ollama pull qwen2.5:7b       # Si GPU < 8GB VRAM — ~5GB
ollama pull nomic-embed-text # Embeddings RAG — obligatoire, léger (~270MB)
```

### Interface abstraite (immuable)
```csharp
// Orion.Core/Interfaces/ILLMClient.cs — IMMUABLE
public interface ILLMClient
{
    Task<ApiResponse<LLMResponse>> CompleteAsync(LLMRequest request, CancellationToken ct = default);
    Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default);
    bool IsAvailable();
    LLMProvider Provider { get; }
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
conversations      -- sessions de chat
messages           -- messages individuels
memory_vectors     -- embeddings pgvector (RAG)
user_profile       -- profil Yawo (projets, préférences, contexte)
behavior_patterns  -- patterns comportementaux observés par ORION
tool_executions    -- log des appels tools
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
│  tool_input JSON │         │  behavior_patterns    │
│  tool_result JSON│         │  ────────────────     │
│  created_at      │         │  id (UUID) PK         │
└────────┬─────────┘         │  pattern_type          │
         │ 1                 │  observed_at           │
         │                   │  context               │
         │ N                 │  orion_response        │
┌────────▼─────────┐         └──────────────────────┘
│  tool_executions │
│  (log appels)    │
└──────────────────┘
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
                    SSE stream ──► [Frontend ResponseText.tsx]
```

### Diagramme Voix — Niveaux d'implémentation
```
NIVEAU 1 (Phase 1) — Web Speech API, 0 coût
─────────────────────────────────────────────
Micro → [SpeechRecognition API] → texte → backend
texte ← [SpeechSynthesis API] ← réponse ORION
Qualité : correcte | Latence : 1-2s | Coût : 0€

NIVEAU 2 (Phase 4) — Whisper.net + Kokoro ONNX, 0 coût, 100% local
──────────────────────────────────────────────────────────────────────
Whisper.net  = wrapper .NET du modèle Whisper open source d'OpenAI
               STT (Speech-to-Text = audio → texte) — local, gratuit
Kokoro ONNX  = modèle TTS via Microsoft.ML.OnnxRuntime
               TTS (Text-to-Speech = texte → voix naturelle) — local, gratuit
               Modèle : hexgrad/Kokoro-82M → kokoro-v0_19.onnx (~300MB) Hugging Face
               Fallback : System.Speech (SAPI natif Windows) si ONNX non configuré

Micro → [MediaRecorder / WebRTC] → fichier audio → backend
      → [Whisper.net local] → transcription précise
      → [ConversationAgent + LLM] → réponse texte
      → [Kokoro ONNX local] → audio naturel
      → [AudioContext frontend] → tu entends ORION
Qualité : excellente | Latence : 1-3s | Coût : 0€
Stack WebRTC déjà maîtrisée via ShadowCat

NIVEAU 3 (Phase 5) — Temps réel < 1s
──────────────────────────────────────
Micro → [VAD @ricky0123/vad-web] → détecte fin de parole automatiquement
      → WebRTC stream → Whisper.net streaming → LLM streaming
      → Kokoro ONNX streaming → AudioContext
Qualité : iron man | Latence : <1s | Coût : 0€
```

## Tools — Comment ça fonctionne

### Principe
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

### Tools Phase 3 (Connecteurs externes + Internet)
```
get_emails                 Gmail API — emails non lus + résumé
send_email                 Gmail API — envoie un email
get_calendar               Google Calendar — événements du jour/semaine
web_search                 Recherche web (SerpAPI / Brave Search API)
web_fetch                  Récupère le contenu texte d'une URL
web_browse                 Navigation interactive via Playwright
                           (Playwright = contrôle un vrai navigateur Chromium en code)
screenshot_page            Capture une page web → ORION "voit" la page
check_render_deploy        Statut et logs déploiement Render
check_vercel_deploy        Statut et logs déploiement Vercel
get_supabase_logs          Logs d'erreur Supabase (ShiftStar, ORION)
send_whatsapp              WhatsApp Business API — message rapide
linkedin_draft             Prépare un post LinkedIn (texte, hashtags)
```

### Tools Mémoire — ORION se gère lui-même
```
memory_save          Sauvegarde un fait important (ORION décide seul quand c'est critique)
memory_update        Met à jour un souvenir existant (évite les doublons)
memory_forget        Supprime un souvenir obsolète ou incorrect
memory_reflect       Synthèse hebdomadaire autonome — appelé chaque dimanche 23h
profile_update       Met à jour user_profile directement (priorités, préférences)
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
Orion.Core      → ne dépend de rien
Orion.Business  → dépend de Orion.Core
Orion.Data      → dépend de Orion.Core
Orion.Api       → dépend de Orion.Business

Orion.Daemon.Core    → ne dépend de rien (propre au daemon)
Orion.Daemon.Actions → dépend de Orion.Daemon.Core
Orion.Daemon         → dépend de Orion.Daemon.Core + Orion.Daemon.Actions

Contrat backend ↔ daemon = JSON sur WebSocket (pas de DLL partagée)
Chaque côté définit ses propres types DaemonCommand / DaemonResponse
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
│       ├── ApiResponse.cs         # Pattern ShadowCat — retourné par Business
│       ├── ChatResponse.cs
│       ├── BriefingDto.cs
│       ├── ToolCallDto.cs
│       ├── ToolResult.cs
│       └── LLMResponse.cs
│
├── Interfaces/
│   ├── Repositories/
│   │   ├── IGenericRepository.cs  # Pattern ShadowCat — CRUD + pagination
│   │   ├── IConversationRepository.cs
│   │   ├── IMessageRepository.cs
│   │   ├── IMemoryRepository.cs   # + SearchSimilarAsync() pgvector
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
│       ├── IDaemonClient.cs       # Contrat — implémenté par DaemonWebSocketClient
│       ├── DaemonCommand.cs       # Backend construit et sérialise en JSON → WSS
│       └── DaemonResponse.cs      # Backend désérialise le JSON reçu du daemon
│
├── Common/
│   └── Result.cs                  # Result<T> usage interne Data → Business
│
└── Configuration/
    ├── OllamaOptions.cs
    ├── AnthropicOptions.cs
    └── DaemonOptions.cs           # Token, RenderWsUrl — côté backend
```

### Contrats Core — extraits clés

```csharp
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

public interface IMemoryRepository : IGenericRepository<MemoryVector, Guid>
{
    Task<IEnumerable<MemoryVector>> SearchSimilarAsync(
        float[] embedding, int topK = 5, CancellationToken ct = default);
    Task<IEnumerable<MemoryVector>> GetBySourceAsync(
        string source, CancellationToken ct = default);
}

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
```

### Règle de retour par couche — IMMUABLE
```
Data            T? / IEnumerable<T>  Repositories : données brutes
Business        ApiResponse<T>       Connaît le sens métier de l'erreur
Controller      IActionResult        Unwrap ApiResponse → HTTP status code
```

### Orion.Business
```
Orion.Business/
├── Agents/
│   ├── ConversationAgent.cs
│   ├── MemoryAgent.cs
│   ├── ToolAgent.cs
│   └── BriefingAgent.cs
├── LLM/
│   ├── LLMRouter.cs
│   ├── OllamaClient.cs
│   ├── AnthropicClient.cs
│   └── PromptBuilder.cs
├── Tools/
│   ├── ToolRegistry.cs
│   ├── ShiftStar/
│   │   ├── GetShiftStarStatsTool.cs
│   │   ├── GetShiftStarVotesTool.cs
│   │   ├── GetShiftStarMrrTool.cs
│   │   ├── GetShiftStarTenantsTool.cs
│   │   └── CreateChallengeTool.cs
│   ├── System/
│   │   ├── MorningBriefingTool.cs
│   │   ├── SendNotificationTool.cs
│   │   └── OpenAppTool.cs
│   ├── Internet/
│   │   ├── WebSearchTool.cs
│   │   ├── WebFetchTool.cs
│   │   ├── WebBrowseTool.cs
│   │   └── ScreenshotTool.cs
│   └── Memory/
│       ├── MemorySaveTool.cs
│       ├── MemoryUpdateTool.cs
│       ├── MemoryForgetTool.cs
│       ├── MemoryReflectTool.cs
│       └── ProfileUpdateTool.cs
├── Daemon/
│   ├── DaemonWebSocketClient.cs
│   └── DaemonActionValidator.cs
└── Services/
    ├── EmbeddingService.cs
    ├── SttService.cs              # Whisper.net — audio → texte
    ├── TtsService.cs              # Kokoro ONNX — texte → audio (fallback SAPI)
    └── PushNotificationService.cs
```

### Orion.Data
```
Orion.Data/
├── Repositories/
│   ├── GenericRepository.cs
│   ├── ConversationRepository.cs
│   ├── MessageRepository.cs
│   ├── MemoryRepository.cs        # + SearchSimilarAsync pgvector
│   └── UserProfileRepository.cs
├── UnitOfWork/
│   └── UnitOfWork.cs
├── Context/
│   └── SupabaseContext.cs
└── Mappings/
    └── SupabaseMappings.cs
```

### Orion.Api
```
Orion.Api/
├── Controllers/
│   ├── ChatController.cs
│   ├── MemoryController.cs
│   ├── DaemonController.cs
│   ├── ToolsController.cs
│   ├── BriefingController.cs
│   ├── VoiceController.cs
│   └── HealthController.cs
├── Middleware/
│   ├── AuthMiddleware.cs
│   ├── ErrorHandlingMiddleware.cs
│   ├── LoggingMiddleware.cs
│   └── DaemonWebSocketMiddleware.cs
├── Program.cs
└── appsettings.json
```

## ORION Daemon — Agent Système Windows

### Rôle
Programme .NET 9 **Worker Service** installé comme service Windows.
Tourne en arrière-plan 24/7, initie une connexion WebSocket vers le backend Render et attend des commandes.
Type de projet : Worker Service (.NET) — PAS ASP.NET Core API.

### Structure — 3 projets
```
orion/daemon/
│
├── Orion.Daemon/
│   ├── Program.cs
│   ├── DaemonWorker.cs
│   ├── WebSocket/
│   │   ├── DaemonWebSocketManager.cs    # Initie WSS vers Render + reconnexion auto
│   │   └── DaemonMessageHandler.cs
│   ├── Watchers/                        # Surveillance autonome permanente
│   │   ├── ActivityWatcher.cs           # Inactivité clavier/souris
│   │   ├── TimeWatcher.cs               # Crons locaux (repas, pause, nuit)
│   │   ├── ProcessWatcher.cs            # Apps ouvertes détectées
│   │   └── SystemWatcher.cs             # CPU, RAM, réseau
│   ├── Notifiers/                       # Canaux de sortie sans app ouverte
│   │   ├── WindowsNotifier.cs           # Notifications Windows natives
│   │   └── SapiSpeaker.cs              # TTS Windows natif (System.Speech.Synthesis)
│   │                                    # 0 NuGet, 0 coût, parle sans app ouverte
│   └── appsettings.json
│
├── Orion.Daemon.Core/
│   ├── Entities/
│   │   ├── DaemonCommand.cs
│   │   └── DaemonResponse.cs

## Architecture Production vs Développement

### Production (Render + Vercel)
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                               PRODUCTION                                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  FRONTEND (Vercel)        BACKEND (Render)           DAEMON (Ta machine)   │
│  ─────────────────        ────────────────            ───────────────────   │
│  https://orion.vercel.app  https://orion-api.onrender.com   (Windows local)  │
│                                                                              │
│       │                           │                           │               │
│       │    HTTPS/API            │                           │               │
│       └────────────────────────►│                           │               │
│                                 │                           │               │
│                                 │◄──────────────────────────┘               │
│                                 │     WSS wss://orion-api.onrender.com/daemon│
│                                 │     (WebSocket sécurisé)                  │
│                                 │                                           │
│       │◄────────────────────────┤                                           │
│       │    SSE /api/proactivenotification/stream                            │
│       │    (Server-Sent Events)                                             │
│                                                                              │
│  FLOW:                                                                       │
│  1. Daemon détecte pattern → envoie au backend via WSS                     │
│  2. Backend broadcast aux frontend connectés via SSE                         │
│  3. Frontend reçoit notif → TTS Web Speech API                               │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Développement Local (tout sur localhost)
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              DÉVELOPPEMENT                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  FRONTEND              BACKEND                DAEMON                         │
│  localhost:5173        localhost:5107          localhost:(adaptive)          │
│                                                                              │
│       │                    │                     │                           │
│       │  HTTP/API          │                     │                           │
│       └───────────────────►│                     │                           │
│                            │                     │                           │
│                            │◄────────────────────┘                           │
│                            │   WS ws://localhost:5107/daemon                │
│                            │   (WebSocket non sécurisé)                     │
│                            │                                                 │
│       │◄───────────────────┤                                                 │
│       │   SSE /api/proactivenotification/stream                               │
│                                                                              │
│  Config:                                                                     │
│  - appsettings.json → "RenderWsUrl": "ws://localhost:5107/daemon"            │
│  - endpoints.ts → API_BASE = "http://localhost:5107"                         │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Communication Daemon ↔ Backend

**WebSocket (Production & Dev)**
- Daemon initie la connexion vers `/daemon` endpoint
- Authentification par token header `X-Daemon-Token`
- Heartbeat bidirectionnel pour détecter déconnexions
- Reconnexion automatique avec backoff exponentiel

**Proactive Notifications (Daemon → Frontend)**
```
Daemon detecte pattern
      ↓
POST /api/proactivenotification/notify (HTTP)  ← Fallback si WS down
OU envoie via WebSocket actif
      ↓
Backend stocke en mémoire + broadcast SSE
      ↓
Frontend reçoit via EventSource
      ↓
TTS Web Speech API triggered
```

### Déploiement Backend (Render)

1. **Dockerfile** présent à la racine backend
2. **Variables d'environnement Render**:
   ```
   ASPNETCORE_ENVIRONMENT=Production
   SUPABASE_URL=...
   SUPABASE_SERVICE_KEY=...
   ANTHROPIC_API_KEY=...
   DAEMON_TOKEN=...(secret partagé avec daemon)
   ```
3. **WebSocket support**: Render free tier supporte WSS natif
4. **URL**: `wss://orion-api.onrender.com/daemon`

### Déploiement Frontend (Vercel)

1. **Build**: `npm run build` → `dist/` uploadé
2. **Variables d'environnement**:
   ```
   VITE_API_URL=https://orion-api.onrender.com
   ```
3. **PWA**: Service Worker auto-généré par Vite PWA plugin
4. **URL**: `https://orion.vercel.app`

### Déploiement Daemon (Machine Windows)

**Option A: Service Windows (Production)**
```powershell
# Build Release
cd daemon/Orion.Daemon
dotnet publish -c Release -o ./publish

# Install service Windows
sc create OrionDaemon binPath="C:\Path\To\publish\Orion.Daemon.exe"
sc start OrionDaemon
```

**Option B: Exécutable autonome**
```powershell
dotnet publish -c Release --self-contained -r win-x64
# → Orion.Daemon.exe standalone, pas besoin de runtime .NET installé
```

**Configuration Production daemon**:
```json
// appsettings.json
{
  "Daemon": {
    "RenderWsUrl": "wss://orion-api.onrender.com/daemon",
    "Token": "same-secret-as-render-env",
    "ReconnectDelayMs": 5000,
    "MaxReconnectDelayMs": 60000
  }
}
```

### Sécurité Production

| Élément | Protection |
|---------|-----------|
| Daemon → Backend | Token secret partagé (header) |
| Backend → Supabase | Service Key (backend uniquement) |
| Frontend → Backend | JWT Auth (à implémenter) |
| WebSocket | WSS (TLS) obligatoire en prod |
| SSE | Via HTTPS avec auth token |

### Hooks Frontend Pattern

Tous les hooks utilisent `axios` + `endpoints.ts`:

```typescript
// ❌ INTERDIT - Pas de fetch direct
const res = await fetch('/api/chat');

// ✅ OBLIGATOIRE - Via apiClient
import { apiClient } from '../services/api';
import { ENDPOINTS } from '../config/endpoints';
const res = await apiClient.post(ENDPOINTS.chat.send, data);
```

## Structure Complète Daemon

```
├── Orion.Daemon/
│   ├── Workers/
│   │   └── DaemonWorker.cs
│   ├── Watchers/
│   │   ├── ActivityWatcher.cs
│   │   ├── TimeWatcher.cs
│   │   ├── ProcessWatcher.cs
│   │   ├── SystemWatcher.cs
│   │   └── AdaptiveWatcher.cs       # Auto-learning
│   ├── Notifiers/
│   │   ├── WindowsToastNotifier.cs  # Toast modernes Win 10/11
│   │   ├── WindowsNotifier.cs       # Fallback MessageBox
│   │   ├── PowerShellTtsNotifier.cs # TTS via PowerShell/SAPI 5
│   │   └── KokoroSpeaker.cs         # TTS neuronal (si modèle ONNX)
│   └── appsettings.json
│
├── Orion.Daemon.Core/
│   ├── Entities/
│   │   ├── DaemonCommand.cs
│   │   └── DaemonResponse.cs
│   ├── Interfaces/
│   │   ├── IAction.cs
│   │   └── IActionRegistry.cs
│   └── Configuration/
│       └── DaemonOptions.cs
│
└── Orion.Daemon.Actions/
    ├── ActionRegistry.cs
    ├── OpenAppAction.cs
    ├── OpenFileInEditorAction.cs
    ├── RunScriptAction.cs
    ├── LaunchClaudeAction.cs
    ├── OpenBrowserUrlAction.cs
    ├── GetSystemStatusAction.cs
    ├── ReadFileAction.cs
    ├── WriteFileAction.cs
    ├── GitStatusAction.cs
    └── GitCommitAction.cs
```

### Autonomie — Flux proactif
```
14h23 — ActivityWatcher : inactif depuis 3h + pattern skip_meal
      → POST backend /trigger/proactive
      → LLM génère une réponse contextuelle
      → WindowsNotifier : notification Windows native
      → SapiSpeaker : ORION parle via hauts-parleurs
      → Tout ça sans ouvrir l'app
```

### Sens de la connexion — CRITIQUE
```
Daemon (PC local) ──── WSS connect ────► Backend (Render)
C'est le DAEMON qui initie — pas l'inverse.
Pas de problème firewall, pas de problème IP dynamique.
```

### Installation daemon
```powershell
cd orion/daemon
dotnet publish Orion.Daemon -c Release -r win-x64 --self-contained -o C:\orion\daemon
sc create OrionDaemon binPath="C:\orion\daemon\Orion.Daemon.exe" start=auto
sc start OrionDaemon
sc query OrionDaemon  # vérifier STATE: RUNNING
```

## Frontend — PWA

### Pourquoi React + Vite
- PWA pure, même stack ShiftStar, pas de SSR nécessaire
- Vite = build ultra-rapide, HMR instantané

---

## Philosophie UI — ORION est un organisme, pas une app

```
ORION n'a pas de pages. ORION n'a pas de navigation.
ORION est une entité vivante avec laquelle tu interagis.
Une seule surface. L'entité est le centre de gravité.
```

### Ce qui N'EXISTE PAS
```
✗ Sidebar de navigation
✗ Bulles de chat style iMessage / ChatGPT
✗ Pages séparées
✗ Header / footer
✗ Input visible en permanence
✗ Boutons classiques
```

### Ce qui EXISTE
```
✓ Entité centrale vivante — respire, pulse, réagit
✓ Fond 3D animé permanent (Three.js + particules)
✓ Texte émerge sous l'entité, disparaît doucement
✓ Données holographiques flottent en 3D autour de l'entité
✓ Input slide depuis le bas sur tap — invisible au repos
✓ Voix : appui long → mode écoute immédiat
✓ Gestes mains via caméra (MediaPipe) — optionnel
✓ Mode light (#f9f8ff) / dark (#0d0d14) — toggle discret
```

### Interactions
```
Tap court entité       → input texte slide depuis le bas
Appui long entité      → mode voix immédiat
Tap ailleurs/Escape    → input disparaît
Swipe up               → overlay mémoire
Swipe down             → overlay briefing
Double tap entité      → overlay settings

Gestes mains (MediaPipe — optionnel) :
  Paume ouverte vers caméra  → ORION écoute
  Poing fermé                → ORION se tait
  Pointer vers élément 3D    → sélectionne
  Glisser main               → déplace carte holographique
  Pinch (pouce + index)      → attrape élément
  Écarter doigts             → zoom
```

### États de l'entité
```
Idle       → respire lentement, anneaux tournent doucement
Écoute     → anneaux s'accélèrent, ondes sonores, particules convergent
Réfléchit  → couleur plus soutenue, pulsation rapide
Répond     → texte émerge, données 3D flottent autour
Daemon     → flash bref blanc → violet, confirmation 2s
```

### Stack animation + 3D
```
Three.js (@react-three/fiber)  → scène 3D WebGL — entité et données holographiques
@react-three/drei              → Float (apesanteur), Billboard, Text3D, OrbitControls
Canvas API                     → fond particules connectées (2D, natif)
Framer Motion                  → transitions, drag 3D, spring physics
Web Audio API                  → amplitude micro → réaction visuelle entité
CSS animations                 → breathing, rotation anneaux
MediaPipe (@mediapipe/hands)   → détection gestes mains via caméra (Phase 5)
                                 21 points par main, 30fps, 0 serveur
```

### Structure Frontend Complète
```
frontend/
├── public/
│   ├── manifest.json
│   ├── sw.js
│   └── icons/
│
└── src/
    ├── algorithms/
    │   ├── vadProcessor.ts        # Voice Activity Detection
    │   ├── audioAnalyser.ts       # Web Audio API → amplitude → entité
    │   ├── particleEngine.ts      # Canvas API — particules fond
    │   └── handTracker.ts         # MediaPipe — détection gestes mains (Phase 5)
    │
    ├── components/
    │   ├── entity/
    │   │   ├── OrionEntity.tsx    # Entité 3D centrale (Three.js)
    │   │   │                      # tap court=input | appui long=voix
    │   │   ├── EntityRings.tsx    # Anneaux 3D rotatifs
    │   │   ├── EntityCore.tsx     # Noyau qui pulse
    │   │   └── SoundWaves.tsx     # Ondes sonores mode voix
    │   ├── hologram/
    │   │   ├── HologramCard.tsx   # Carte 3D flottante (Float + Billboard)
    │   │   │                      # Données qui flottent autour de l'entité
    │   │   ├── HologramText.tsx   # Texte 3D dans l'espace
    │   │   └── HologramChart.tsx  # Graphique 3D flottant (stats ShiftStar...)
    │   ├── response/
    │   │   ├── ResponseText.tsx   # Texte SSE mot par mot
    │   │   ├── DataFloat.tsx      # Orchestrateur données holographiques
    │   │   └── ToolCallHint.tsx   # Indicateur tool en cours
    │   ├── input/
    │   │   ├── SlideInput.tsx     # Input caché — slide up sur tap entité
    │   │   └── VoiceWave.tsx      # Onde amplitude enregistrement
    │   ├── overlay/
    │   │   ├── MemoryOverlay.tsx  # Swipe up
    │   │   ├── BriefingOverlay.tsx # Swipe down
    │   │   └── SettingsOverlay.tsx # Double tap
    │   └── canvas/
    │       ├── ParticleCanvas.tsx # Fond particules 2D
    │       └── Scene3D.tsx        # Scène Three.js principale
    │
    ├── hooks/
    │   ├── useOrionEntity.ts      # État entité (idle/listening/thinking/responding)
    │   ├── useAudioAmplitude.ts   # Web Audio API → amplitude temps réel
    │   ├── useChat.ts             # Envoie message, reçoit SSE
    │   ├── useStream.ts           # Lecture SSE token par token
    │   ├── useVoice.ts            # getUserMedia, MediaRecorder, WebRTC
    │   ├── useVAD.ts              # Détection fin de parole
    │   ├── useGestures.ts         # tap, long press, swipe — interactions entité
    │   ├── useHandTracking.ts     # MediaPipe — gestes mains caméra (Phase 5)
    │   ├── usePushNotif.ts        # Service Worker + push
    │   └── useOrionStatus.ts      # Ping backend : LLM, daemon
    │
    ├── context/
    │   ├── EntityContext.tsx      # État global entité
    │   ├── OrionStatusContext.tsx # LLM provider, daemon up/down
    │   └── ThemeContext.tsx       # light / dark
    │
    ├── services/
    │   ├── api.ts                 # Axios instance centralisée
    │   ├── chatService.ts
    │   ├── memoryService.ts
    │   ├── briefingService.ts
    │   ├── daemonService.ts
    │   ├── healthService.ts
    │   ├── voiceApi.ts
    │   └── toolsService.ts
    │
    ├── config/
    │   └── endpoints.ts           # ENDPOINTS centralisés
    │
    ├── types/
    │   ├── api/apiResponse.ts     # Miroir TypeScript ApiResponse<T> .NET
    │   ├── dto/
    │   │   ├── chatDto.ts
    │   │   ├── memoryDto.ts
    │   │   ├── briefingDto.ts
    │   │   ├── toolDto.ts
    │   │   └── voiceDto.ts
    │   └── models/
    │       ├── entityState.ts     # 'idle'|'listening'|'thinking'|'responding'
    │       ├── message.ts
    │       └── orionStatus.ts
    │
    ├── utils/
    │   ├── animationUtils.ts
    │   ├── audioUtils.ts
    │   └── dateUtils.ts
    │
    ├── App.tsx                    # Surface unique — pas de Router
    ├── main.tsx
    ├── index.css
    └── vite-env.d.ts

# Pas de pages/ — surface unique, overlays uniquement
```

### Dépendances frontend
```bash
# 3D holographique
npm install three
npm install @react-three/fiber     # Three.js pour React
npm install @react-three/drei      # Float, Billboard, Text3D, OrbitControls

# Animations
npm install framer-motion          # Transitions, drag, spring physics

# Voix
npm install @ricky0123/vad-web     # Voice Activity Detection

# Gestes mains (Phase 5)
npm install @mediapipe/hands       # Détection 21 points par main
npm install @mediapipe/camera_utils

# PWA
npm install vite-plugin-pwa
```

### Règle App.tsx — surface unique
```tsx
<ThemeProvider>
  <EntityProvider>
    <ParticleCanvas />          {/* fond particules 2D — z-index 0 */}
    <Scene3D>                   {/* scène Three.js — z-index 1 */}
      <OrionEntity />           {/* entité centrale 3D */}
      <HologramCard />          {/* données 3D flottantes */}
    </Scene3D>
    <ResponseText />            {/* texte émergent — z-index 2 */}
    <SlideInput />              {/* input caché — slide up sur tap */}
    <MemoryOverlay />           {/* swipe up — z-index 10 */}
    <BriefingOverlay />         {/* swipe down — z-index 10 */}
    <SettingsOverlay />         {/* double tap — z-index 10 */}
  </EntityProvider>
</ThemeProvider>
```

## Déploiement

### Backend (Render)
```
Service : Web Service
Runtime : Docker
Health check : GET /health
Variables :
  SUPABASE_URL=
  SUPABASE_SERVICE_KEY=
  ANTHROPIC_API_KEY=
  OLLAMA_URL=
  DAEMON_WS_TOKEN=
  JWT_SECRET=
```

### Frontend (Vercel)
```
Framework : Vite
Build : npm run build
Variables :
  VITE_API_URL=https://orion-api.onrender.com
  VITE_WS_URL=wss://orion-api.onrender.com
```

## Dev Local
```bash
# Terminal 1
cd backend && dotnet run --project Orion.Api
# http://localhost:5000

# Terminal 2
cd daemon && dotnet run --project Orion.Daemon

# Terminal 3
cd frontend && npm run dev
# http://localhost:5173

# Ollama — déjà service Windows
```

## Variables d'environnement (.env.example)
```env
SUPABASE_URL=https://xxx.supabase.co
SUPABASE_SERVICE_KEY=eyJ...
ANTHROPIC_API_KEY=sk-ant-...
OLLAMA_BASE_URL=http://localhost:11434
OLLAMA_MODEL=qwen2.5:14b
DAEMON_WS_URL=ws://localhost:5001
DAEMON_WS_TOKEN=secret-token-orion
JWT_SECRET=orion-jwt-secret-change-this

VITE_API_URL=http://localhost:5000
VITE_WS_URL=ws://localhost:5000
```

## Roadmap

### Phase 1 — Core MVP
- [ ] Backend : LLMRouter + ConversationAgent + Mémoire RAG
- [ ] Backend : Tools ShiftStar
- [ ] Frontend : Entité animée + SSE stream
- [ ] Morning briefing automatique

### Phase 2 — Daemon système
- [ ] Daemon Windows Service + Watchers + Notifiers
- [ ] Tools système (open_app, run_script...)
- [ ] WebSocket sécurisé backend ↔ daemon

### Phase 3 — Connecteurs + Internet
- [ ] Gmail, Calendar
- [ ] web_search, web_fetch, web_browse (Playwright)
- [ ] Tools mémoire autonomes (memory_save, memory_reflect...)

### Phase 4 — Voix
- [ ] Whisper.net STT (audio → texte, local)
- [ ] Kokoro ONNX TTS (texte → voix naturelle, local)
  Fallback : System.Speech (SAPI natif Windows, 0 NuGet)
- [ ] VAD @ricky0123/vad-web
- [ ] WebRTC (réutilisé de ShadowCat)

### Phase 5 — 3D holographique + gestes
- [ ] Three.js / @react-three/fiber — scène 3D
- [ ] HologramCard — données flottantes en 3D
- [ ] MediaPipe — gestes mains via caméra
  (paume ouverte, pointer, pinch, glisser)

### Phase 6 — HexaNexus
- [ ] Widget ORION dans HexaNexus dashboard
- [ ] Auth unifiée

## Règles de développement
- **Repository Pattern** obligatoire couche Data
- **Strict TypeScript** — no any
- **ILLMClient** : toujours passer par LLMRouter, jamais direct
- **ITool** : tout tool implémente ITool + ToolRegistry
- **Daemon** : toute action dans la whitelist avant implémentation
- **Mémoire** : toute conversation persistée — aucune exception
- **Logs** : tool call, daemon action, LLM fallback — tout loggué
- **Frontend** : axios via api.ts, endpoints.ts centralisé, jamais fetch direct
- **DTOs** : toujours dans Orion.Core, jamais inline dans controllers
- **Endpoints** : toute nouvelle route backend = mise à jour endpoints.ts frontend
ENDOFFILE