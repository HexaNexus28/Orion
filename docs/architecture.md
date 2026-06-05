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
Orion.Core/        Entities · DTOs (Requests, Responses: ApiResponse<T>, StreamContext, …) ·
                   Interfaces (Repositories, Agents, LLM: ILLMClient/ILLMRouter, Services, Tools,
                   Daemon) · Common/Result.cs · Configuration (Ollama/Anthropic/DaemonOptions)
Orion.Business/    Agents (Conversation, Memory, Tool, Briefing) · LLM (Router, OllamaClient,
                   AnthropicClient, PromptBuilder) · Tools (ShiftStar/System/Internet/Memory) ·
                   Daemon (DaemonWebSocketClient, ActionValidator) · Services (Embedding, Whisper,
                   VoiceNotification, PushNotification)
Orion.Data/        Repositories (Generic + Conversation/Message/Memory/UserProfile) · UnitOfWork ·
                   Context/SupabaseContext · Mappings
Orion.Api/         Controllers (Chat, Memory, Daemon, Tools, Briefing, Voice, ProactiveNotification,
                   Health) · WebSockets (VoiceWebSocketHandler + Middleware) · Middleware (Auth,
                   ErrorHandling, Logging, DaemonWebSocket) · Program.cs
```

Contrats clés (signatures exactes dans `Orion.Core/Interfaces`) :
- `IGenericRepository<T,TId>` : CRUD + `GetPagedAsync` + `GetWithIncludesAsync` + `SaveChangesAsync`
- `IMemoryRepository : IGenericRepository<MemoryVector,Guid>` + `SearchSimilarAsync(float[], topK)`
- `IUnitOfWork` : repos + transactions + `ExecuteInTransactionAsync`
- `IConversationAgent` : `PrepareStreamAsync → ApiResponse<StreamContext>` puis
  `StreamLLMAsync(StreamContext) → IAsyncEnumerable<string>`

## LLM — Multi-Provider (tout via Ollama, jamais d'appel direct)

```
Primary    : deepseek-v4-flash:cloud  → Ollama Cloud (tier Free, quota temps-GPU reset 5h)
Fallback   : llama3.2:3b              → Local (illimité, 0€, si quota cloud épuisé / hors-ligne)
Embeddings : nomic-embed-text (768 dims) → `ollama pull nomic-embed-text` requis
Routing    : LLMRouter.cs — TOUJOURS passer par ILLMRouter
```

Config : `backend/Orion.Api/appsettings.json` → section `Ollama` (`Model`, `FallbackModel`, `BaseUrl`).
**Les noms DOIVENT exister dans `ollama list`** sinon chaque tour échoue (404). Note Ollama Cloud :
pas de tarif par token ; abonnement par paliers (Free/Pro $20/Max $100) avec quotas temps-GPU.
Les modèles 236B+ (ex. qwen3-coder:480b) sont gated derrière Pro/Max.

## Mémoire

Court terme = RAM (~20 msgs). Long terme = Supabase pgvector (RAG).
Tables : `conversations, messages, memory_vectors, user_profile, behavior_patterns, tool_executions`.
Flux : embed message → pgvector top-5 → injecte dans prompt → LLM → sauvegarde + embedding.
RAG est **non-bloquant** (try/catch dans `BuildRelevantMemoriesAsync` → liste vide si embeddings down).
Persistance conversation = **obligatoire** (DB inaccessible → `ApiResponse` 503, le tour échoue).

## Architecture Prod vs Dev

| | Frontend | Backend | Daemon |
|---|---|---|---|
| **Prod** | Vercel `orion.vercel.app` | Render `orion-api.onrender.com` (WSS) | Windows local |
| **Dev** | `localhost:5173` | `localhost:5107` | local adaptatif |

Flux : Frontend ─HTTPS/API→ Backend · Daemon ─WSS `/daemon`→ Backend (le daemon initie, pas de souci
firewall/IP dynamique) · Backend ─SSE `/api/proactivenotification/stream`→ Frontend.

Sécurité : Daemon→Backend token partagé (`X-Daemon-Token`) · Backend→Supabase service key (backend
only) · Frontend→Backend JWT (à implémenter) · WSS/TLS en prod.

Voir [deployment.md](deployment.md) pour le déploiement détaillé.
