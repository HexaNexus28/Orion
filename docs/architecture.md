# Architecture — ORION

## Règle 4 couches — IMMUABLE

```
Orion.Core      → ne dépend de rien            (entities, DTOs, interfaces)
Orion.Business  → dépend de Core               (services, agents, LLM, tools)
Orion.Data      → dépend de Core               (repositories, DbContext, migrations)
Orion.Api       → dépend de Business           (controllers, middleware, WS handlers)

Orion.Daemon.Core    → ne dépend de rien
Orion.Daemon.Actions → dépend de Daemon.Core
Orion.Daemon         → dépend de Daemon.Core + Daemon.Actions

Retour par couche :
  Data        → T? / IEnumerable<T>     (données brutes)
  Business    → ApiResponse<T>          (sens métier de l'erreur)
  Controller  → IActionResult           (unwrap StatusCode uniquement)
  Result<T>   → usage interne Data → Business (Orion.Core/Common)
```

Contrat backend ↔ daemon = JSON + Binary sur WebSocket (pas de DLL partagée). Chaque côté
définit ses propres types `DaemonCommand` / `DaemonResponse`.

## Structure Backend

```
Orion.Core/        Entities · DTOs (Requests, Responses: ApiResponse<T>, StreamContext, HudCard, …) ·
                   Interfaces (Repositories, Agents: IAgentLoop, LLM: ILLMAgentClient, Services,
                   Tools: ITool/IToolInvoker/IToolRegistry, Daemon) · Common/Result.cs ·
                   Configuration (Ollama/Nim/Embedding/Transcription/Auth/Daemon/Internet Options)
Orion.Business/    Agents (AgentLoop, ConversationAgent, BriefingAgent) · LLM (LLMCascade,
                   NimAgentClient, OllamaAgentClient, PromptBuilder) · Tools (System/Internet/
                   Memory + ToolRegistry + ToolInvoker) · Daemon (DaemonWebSocketClient,
                   DaemonActionValidator, DeferredActionService) · Services (Memory, Chat,
                   Briefing, Audit, Health, Embedding, Transcription, Voice)
Orion.Data/        Repositories (Generic + Conversation/Message/Memory/UserProfile/DeferredAction) ·
                   UnitOfWork · Context/OrionDbContext · Migrations
Orion.Api/         Controllers (Auth, Chat, Memory, Daemon, DeferredActions, Briefing, Voice,
                   ProactiveNotification, Health) · Authentication (OrionAuth,
                   DaemonAuthenticationHandler) · WebSockets (VoiceWebSocketHandler + Middleware) ·
                   Middleware (ErrorHandling, DaemonWebSocket) · Services (SseClientRegistry,
                   BriefingScheduler, DeferredActionWatcher, HudBroadcastService) · Program.cs
```

Contrats clés (signatures exactes dans `Orion.Core/Interfaces`) :
- `IGenericRepository<T,TId>` : CRUD + `GetPagedAsync` + `GetWithIncludesAsync` + `SaveChangesAsync`
- `IMemoryRepository : IGenericRepository<MemoryVector,Guid>` + `SearchSimilarAsync(float[], topK)`
- `IUnitOfWork` : repos + transactions + `ExecuteInTransactionAsync`
- `IConversationAgent` : `PrepareStreamAsync → ApiResponse<StreamContext>` puis
  `StreamLLMAsync(StreamContext) → IAsyncEnumerable<string>`

## LLM — cascade, et un seul chemin

```
IAgentLoop (AgentLoop)          ← TOUT passe par là. Jamais d'appel LLM direct.
   └── ILLMAgentClient (LLMCascade)   ← streaming AVEC outils
         ├── NimAgentClient      NVIDIA NIM, compatible OpenAI   ← distant, qualité
         └── OllamaAgentClient   local                            ← repli hors-ligne, dégradé
```

**L'ORDRE DU TABLEAU EST LA POLITIQUE** — rien d'autre ne la décide (`Program.cs`). Distant
d'abord pour la qualité, local en dernier pour survivre hors-ligne.

⚠️ `ILLMClient` / `ILLMRouter` sont l'**ancien chemin, sans outils** : ils ne portent pas de
`tool_call` et ne doivent plus être utilisés pour un nouveau développement. L'ancienne cascade
« Ollama Cloud → local » et le client Anthropic ont disparu du code — il n'existe **pas** de
`AnthropicClient` dans le dépôt.

Le modèle réellement servi est **élu au démarrage par `ProbeAsync`**, en APPELANT le fournisseur.
`ollama list` ne prouve rien : il affiche les `:cloud` en cache local même retirés ou verrouillés
par abonnement (vérifié le 2026-08-20 : 7 modèles listés, 7 inutilisables).

`NumCtx` est **obligatoire** : sans elle Ollama dimensionne le cache KV sur le contexte maximum du
modèle (128k) et réclame ~15 Go pour un modèle de 2 Go → HTTP 500 intermittent.

### Embeddings — un espace vectoriel, jamais deux

```
IEmbeddingService → OpenAiCompatibleEmbeddingService → mistral-embed, 1024 dims
```

Ollama a été **retiré du chemin de production** : il n'existe pas sur le VPS, la mémoire y serait
morte en silence. Contrairement au cerveau, un embedding **ne bascule pas à chaud** : chaque modèle
projette dans son propre espace. Changer de fournisseur impose de revectoriser toute la table
(`MemoryRevectorizer`). Modèle et dimension sont écrits À CÔTÉ de chaque vecteur et vérifiés au
démarrage — mélanger deux espaces ne lève aucune erreur et renvoie des résultats absurdes.

### Transcription — même motif

```
IWhisperService → TranscriptionCascade → [ VoxtralTranscriptionService (Mistral), WhisperService (local) ]
```

Voxtral : 0,35 s contre 5,0 s en local sur le même audio (mesure du 2026-08-27), et transcrit
mieux. Le local reste DERRIÈRE pour que la voix survive à une panne du fournisseur.

Configuration : `backend/Orion.Api/appsettings.json` — ⚠️ **gitignoré, donc absent du dépôt**. En
production les valeurs arrivent par variables d'environnement (cf. [deployment.md](deployment.md)).

## Mémoire

Court terme = RAM (~20 msgs). Long terme = Supabase pgvector (RAG).
Tables : `conversations, messages, memory_vectors, user_profile, behavior_patterns, tool_executions`.
Flux : embed message → pgvector top-5 → injecte dans prompt → LLM → sauvegarde + embedding.
RAG est **non-bloquant** (try/catch dans `BuildRelevantMemoriesAsync` → liste vide si embeddings down).
Persistance conversation = **obligatoire** (DB inaccessible → `ApiResponse` 503, le tour échoue).

## Architecture Prod vs Dev

| | Frontend | Backend | Daemon |
|---|---|---|---|
| **Prod** | servi par le backend (`wwwroot`) derrière Nginx | VPS, port loopback, façade Nginx (WSS) | Windows local, session utilisateur |
| **Dev** | `localhost:5173` (Vite) | `localhost:5107` | local adaptatif |

⚠️ Render et Vercel sont des **vestiges** : l'hébergement est un VPS unique derrière Nginx. Le nom
`DaemonOptions.RenderWsUrl` en garde la trace côté daemon.

En production la PWA n'est **pas** hébergée séparément : le bundle construit vit dans `wwwroot` et
le backend le sert lui-même, avant l'authentification (la coquille de l'application n'est pas un
secret — si elle exigeait une session, l'utilisateur n'aurait jamais l'écran pour en ouvrir une).

```
Navigateur ──HTTPS/API──────────────────→ Backend
Navigateur ──WSS /ws/voice (billet)─────→ Backend
Daemon     ──WSS /daemon (X-Daemon-Token)→ Backend   (le daemon INITIE : pas de souci firewall/IP dynamique)
Backend    ──SSE /api/proactivenotification/stream──→ Navigateur
```

## Sécurité — modèle d'authentification

**Une seule porte, deux appelants** (`Orion.Api/Authentication/OrionAuth.cs`) :

| Appelant | Preuve | Pourquoi celle-là |
|---|---|---|
| Propriétaire (navigateur) | JWT signé, obtenu par mot de passe | un navigateur ne peut rien garder de permanent |
| Daemon (machine de confiance) | secret partagé `X-Daemon-Token` | aucun login interactif possible sur un service Windows |

Principes appliqués, à ne pas défaire :

- **Fermé par défaut** — `FallbackPolicy` exige le rôle propriétaire ; une route sans attribut est
  refusée, jamais ouverte. Les exceptions sont explicites : `/api/auth/login`, `/health`, `index.html`.
- **Fail-closed** — un secret absent REFUSE, il n'ouvre pas.
- **Billet de flux** — SSE et WebSocket de navigateur ne portent aucun en-tête : leur jeton passe
  par l'URL, donc c'est un billet de 60 s à **audience distincte**, et le contrôle vaut dans les
  deux sens (jeton de session dans une URL → refusé ; billet hors d'un chemin de flux → refusé).
- **Origines WebSocket** — même source de vérité que le CORS ; liste vide = refus de démarrer.

⚠️ **L'authentification ne couvre que la moitié du problème** : une fois la session ouverte, ce que
le modèle décide part avec les droits complets de l'utilisateur. Le garde-fou correspondant est
`IToolInvoker` + `ITool.IsDestructive`, et son périmètre est incomplet — voir
**[security.md](security.md)**, qui est la référence sur ce sujet.

Voir [deployment.md](deployment.md) pour le déploiement détaillé.
