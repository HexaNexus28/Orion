# ORION — CLAUDE.md

ORION = assistant IA personnel de Yawo Zoglo, futur moteur IA HexaNexus.
Stack : React 19 + Vite (PWA) | .NET 9 backend | Ollama + Claude fallback | Supabase + pgvector | Daemon Windows.
Langue : Français. Voir AGENTS.md pour règles, workflows, phases.

## Architecture

```
orion/
├── backend/           .NET 9 Clean Architecture (Api → Business → Core → Data)
├── frontend/          React 19 + Vite + TailwindCSS + Three.js (PWA)
├── daemon/            .NET 9 Worker Service Windows (3 projets)
├── memory/            schema.sql, seed.sql
├── tools/             JSON Schema definitions
├── CLAUDE.md          CE FICHIER
└── AGENTS.md          Règles, agents, phases, ADRs
```

Contrat backend ↔ daemon = JSON + Binary sur WebSocket (pas de DLL partagée).

## LLM — Multi-Provider (tout via Ollama)

```
Primary:   qwen3.5:cloud       → Ollama Cloud (gratuit, 0 clé API, multimodal, tools, thinking)
Fallback:  ministral-3:cloud   → Ollama Cloud (Mistral, FR natif, rapide)
Offline:   qwen2.5:3b          → Local (si internet down)
Embeddings : nomic-embed-text (768 dims) — obligatoire
Routing : LLMRouter.cs — TOUJOURS passer par ILLMRouter, jamais appel direct
```

## Mémoire

Court terme = RAM (~20 msgs). Long terme = Supabase pgvector (RAG).
Tables : conversations, messages, memory_vectors, user_profile, behavior_patterns, tool_executions.
Flux : embed message → pgvector top-5 → injecte dans prompt → LLM → sauvegarde + embedding.

## Voice Pipeline — Full-Duplex (OPÉRATIONNEL)

```
Frontend (VAD)                          Backend (/ws/voice)
─────────────                           ───────────────────
VAD détecte parole    ──PCM Int16──►    Whisper.net STT
                      ◄──JSON────       onTranscript
                      ◄──JSON────       onLLMChunk (token/token)
                      ◄──JSON────       onLLMDone
AudioContext 24kHz    ◄──Binary───      Kokoro TTS (daemon) WAV

Anti-écho :
- voiceWSResponseRef bloque Web Speech TTS pendant pipeline WS
- window.speechSynthesis.speaking check avant trigger VAD
- Ne jamais activer Web Speech TTS et Kokoro TTS simultanément

TTS dual-mode :
- Mode TEXT (clavier) → Web Speech API (voiceWSResponseRef = false)
- Mode VOICE (WS)     → Kokoro daemon (voiceWSResponseRef = true)
```

## Règle 4 couches — IMMUABLE

```
Orion.Core      → ne dépend de rien
Orion.Business  → dépend de Core
Orion.Data      → dépend de Core
Orion.Api       → dépend de Business

Retour par couche :
  Data        → T? / IEnumerable<T>
  Business    → ApiResponse<T>          (sens métier de l'erreur)
  Controller  → IActionResult           (unwrap StatusCode uniquement)
```

### Diagramme Voix — Pipeline Full-Duplex (OPÉRATIONNEL)
```
ÉTAT ACTUEL : WebSocket bidirectionnel /ws/voice — full-duplex, barge-in, 0 coût
════════════════════════════════════════════════════════════════════════════════════

┌─────────── FRONTEND ───────────┐       ┌─────────── BACKEND ──────────────┐
│                                │       │                                  │
│  VAD (@ricky0123/vad-web)     │  WS   │  VoiceWebSocketHandler.cs        │
│  détecte fin de parole auto   │◄─────►│  /ws/voice bidirectionnel        │
│       │                        │       │       │                          │
│  PCM Int16 chunks             │──────►│  Whisper.net STT (local)         │
│  (streaming temps réel)       │       │       │                          │
│       │                        │       │  ConversationAgent               │
│  onTranscript ← JSON          │◄──────│  PrepareStreamAsync()            │
│  onLLMChunk   ← JSON          │◄──────│  StreamLLMAsync() (token/token)  │
│  onLLMDone    ← JSON          │◄──────│       │                          │
│       │                        │       │  TTS Kokoro (daemon)             │
│  AudioContext (24kHz)         │◄──────│  WAV binaire via WS              │
│  joue les WAV chunks          │       │                                  │
│       │                        │       └──────────────────────────────────┘
│  Barge-in: interrupt WS msg   │───────► CancellationToken annule le tour
│                                │
│  Anti-écho:                    │
│  - voiceWSResponseRef bloque   │
│    Web Speech TTS pendant WS   │
│  - window.speechSynthesis      │
│    .speaking check dans VAD    │
└────────────────────────────────┘

PROTOCOLE WebSocket :
  Client → Server : JSON { type: "start_audio" | "end_audio" | "interrupt" }
  Client → Server : Binary (PCM Int16 chunks)
  Server → Client : JSON { type: "transcript" | "llm_chunk" | "llm_done" | "session" | "error" }
  Server → Client : Binary (WAV audio chunks)

STACK :
  STT : Whisper.net (local, gratuit, ~1.5GB modèle)
  TTS : KokoroSharp.CPU (local, gratuit, ~320MB modèle, voix ff_siwis)
  VAD : @ricky0123/vad-web (browser, WebAssembly)
  LLM : Ollama local → fallback Claude API
```

### Pipeline TTS dual-mode
```
Mode TEXT (input clavier) → Web Speech API (navigateur)
  responseText → speakSentence() phrase par phrase
  voiceWSResponseRef = false

Mode VOICE (WebSocket) → Kokoro (daemon) — optimisé latence
  LLM stream → sentence split (smart: .!? min 20 chars, weak break à 80, force à 150)
  → VoiceNotificationService.SynthesizeAsync() (pipeliné: TTS en // du LLM stream)
  → DaemonClient binary WS → KokoroSpeaker → WAV raw bytes (pas de base64)
  → Backend forward WAV binaire au frontend via WS
  → Frontend: pre-decode pipeline (décode chunk N+1 pendant lecture chunk N)
  voiceWSResponseRef = true → Web Speech TTS désactivé

Prompt voix dédié (ChatRequest.VoiceMode = true):
  - Réponses courtes, orales, sans markdown
  - Connecteurs conversationnels, chiffres parlés
  - Max 3-4 phrases, propose d'approfondir si long
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

Contrat backend ↔ daemon = JSON + Binary sur WebSocket (pas de DLL partagée)
Chaque côté définit ses propres types DaemonCommand / DaemonResponse
Protocole binaire TTS: [36-byte requestId UTF-8] + [raw WAV bytes] (pas de base64)
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
│       ├── StreamContext.cs       # DTO pour PrepareStreamAsync → StreamLLMAsync
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
    ├── WhisperService.cs              # Whisper.net — audio → texte (STT local)
    ├── VoiceNotificationService.cs    # TTS via daemon (Kokoro) — WAV synthesis
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
│   ├── VoiceController.cs                  # Legacy HTTP voice endpoints
│   ├── ProactiveNotificationController.cs  # SSE stream + daemon notify
│   └── HealthController.cs
├── WebSockets/
│   ├── VoiceWebSocketHandler.cs            # /ws/voice full-duplex handler
│   └── VoiceWebSocketMiddleware.cs         # Route /ws/voice requests
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
│   │   ├── WindowsToastNotifier.cs     # Toast Windows 10/11
│   │   ├── WindowsNotifier.cs           # Fallback MessageBox
│   │   ├── PowerShellTtsNotifier.cs     # TTS SAPI5 via PowerShell
│   │   └── KokoroSpeaker.cs            # TTS neuronal KokoroSharp.CPU
│   │                                    # NuGet KokoroSharp.CPU v0.6.6
│   │                                    # Voix: ff_siwis (French female)
│   │                                    # Modèle auto-download ~320MB
│   ├── ProactiveOrchestrator.cs          # Détecte patterns → génère messages → notifie
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
    │   │   ├── HologramCard.tsx           # Carte 3D flottante (Float + Billboard)
    │   │   ├── HologramText.tsx           # Texte 3D SDF dans l'espace
    │   │   ├── HologramChart.tsx          # Graphique 3D flottant
    │   │   └── HologramResponsePanel.tsx  # Panneau réponse holographique
    │   │                                  # Pure Three.js : GLSL shader, SDF Text,
    │   │                                  # particules, wireframe, anneaux orbitaux
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
    │   ├── useStream.ts           # Lecture SSE token par token + appendChunk/setStreaming
    │   ├── useVoice.ts            # LEGACY — getUserMedia, MediaRecorder (remplacé par useVoiceWS)
    │   ├── useVoiceWS.ts          # Full-duplex WebSocket voice — pipeline actif
    │   ├── useVAD.ts              # VAD @ricky0123/vad-web + PCM streaming
    │   ├── useGestures.ts         # tap, long press, swipe — interactions entité
    │   ├── useHandTracking.ts     # MediaPipe — gestes mains caméra (Phase 5)
    │   ├── useOrionNotifications.ts # SSE proactive notifications + Web Speech TTS
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
    │   ├── voiceApi.ts            # LEGACY HTTP voice
    │   └── voiceWebSocket.ts      # WebSocket client /ws/voice
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
    {/* Scène 3D PERMANENTE (z-0) — contient TOUT le rendu 3D */}
    <Scene3D                        {/* fond: Stars + Grid + PostProcessing */}
      responseText={responseText}   {/* → HologramResponsePanel (Float + Billboard) */}
      isStreaming={isStreaming}
      onTap={handleOpenInput}       {/* → OrionCore3D (sphère énergie + anneaux + particules) */}
      onLongPress={processVoiceTurn}
      onDoubleTap={handleOpenSettings}
    />
    <DataCards />                   {/* cartes données HTML — z-10 */}
    <SlideInput />                  {/* input caché — slide up */}
    <MemoryOverlay />               {/* swipe up — z-30 */}
    <BriefingOverlay />             {/* swipe down — z-30 */}
    <SettingsOverlay />             {/* double tap — z-30 */}
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

### Phase 4 — Voix ✅ OPÉRATIONNEL
- [x] Whisper.net STT (audio → texte, local)
- [x] KokoroSharp.CPU TTS (texte → voix naturelle, local, ff_siwis)
- [x] VAD @ricky0123/vad-web (700ms silence timeout)
- [x] WebSocket /ws/voice full-duplex bidirectionnel
- [x] Barge-in (interrupt + CancellationToken)
- [x] Anti-écho (voiceWSResponseRef + speechSynthesis.speaking)
- [x] ConversationAgent refactor: PrepareStreamAsync + StreamLLMAsync
- [x] Proactive notifications: Daemon → SSE → Frontend (camelCase fix)

### Phase 4.5 — Latence voix  ✅ IMPLÉMENTÉ
- [x] P6: Prompt voix dédié — réponses courtes, orales, sans markdown (ChatRequest.VoiceMode)
- [x] P4: Smart sentence splitting — .!? min 20 chars, weak break à 80, force à 150
- [x] P7: TTS pipeliné en parallèle du LLM stream (fire-and-await pattern)
- [x] P5: Frontend pre-decode pipeline (décode chunk N+1 pendant lecture chunk N)
- [x] P3: KokoroSpeaker.SynthesizeStreamAsync — segments via Channel async
- [x] P1: Binary WS daemon↔backend — raw WAV bytes, zéro base64 overhead
- [x] Multi-frame WS accumulation (daemon + backend) — gros messages WAV OK
- [x] IVoiceNotificationService interface + DI propre (R-07 fix)

### Phase 5 — 3D holographique + gestes 🚧 EN COURS
- [x] Three.js / @react-three/fiber — scène 3D (Scene3D.tsx)
- [x] HologramCard, HologramChart — données flottantes en 3D
- [x] HologramResponsePanel — panneau réponse holographique pur Three.js
       (GLSL shader, SDF Text, particules, wireframe, anneaux)
- [ ] Entité ORION migrée en Three.js (actuellement Canvas 2D)
- [ ] MediaPipe — gestes mains via caméra
  (paume ouverte, pointer, pinch, glisser)

### Phase 6 — Intelligence Proactive (Jarvis Awareness)
- [ ] Screen awareness: screenshot périodique → Qwen3.5 vision → ORION sait ce que tu fais
- [ ] Context switching: détecte changement de projet (VS Code, browser) → adapte automatiquement
- [ ] Smart interruptions: ORION ne parle que quand c'est pertinent (scoring urgence)
- [ ] Briefing adaptatif: pas juste morning, mais contextuel (avant meeting, deadline proche)
- [ ] Learning loop: behavior_patterns → prédire besoins → agir avant la demande
- [ ] Ambient mode: ORION tourne en tâche de fond, intervient proactivement
- [ ] Multi-session memory: résume les sessions précédentes pour continuité

### Phase 7 — Capacités Jarvis Avancées
- [ ] Code review automatique: watch git changes → analyse impact → commentaires proactifs
- [ ] Deploy monitoring: vérifie Render/Vercel status → alerte si problème
- [ ] Autonomous task execution: "déploie ShiftStar" → git push + monitor + confirm
- [ ] Multi-tool chaining: ORION combine tools sans intervention (web_search → analyze → summarize)
- [ ] Voice commands système: "ouvre le projet ShiftStar dans VS Code" → daemon action
- [ ] Email/calendar integration: "résume mes emails" → "planifie le meeting" → action directe
- [ ] Playwright automation: ORION navigue le web pour toi (recherche, test, scrape)

### Phase 8 — HexaNexus
- [ ] Widget ORION dans HexaNexus dashboard
- [ ] Auth unifiée
- [ ] ORION comme moteur IA multi-tenant pour tous les clients HexaNexus

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