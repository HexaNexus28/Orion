# ORION — AGENTS.md
# Instructions pour agents IA travaillant sur ce projet

## Rôle de ce fichier
Ce fichier est lu par tout agent IA (Claude Code, Cursor, Windsurf, Copilot) avant d'intervenir sur le projet ORION.
Il définit les règles de comportement, les workflows, les contraintes, et la mémoire de décisions architecturales.

---

## 1. Contexte Projet

```
Projet   : ORION — assistant IA personnel de Yawo Zoglo
Owner    : Yawo Zoglo (contact@shift-star.app)
Univers  : HexaNexus (ShiftStar, ORION, HexaNexus 2.0)
Langue   : Français (réponses ORION) / Anglais (code, commentaires)
Stack    : .NET 9, React 19 + Vite, Supabase, Ollama, Claude API
Niveau   : Développeur avancé — pas d'explications basiques
```

---

## 2. Règles Absolues (ne jamais violer)

```
[RULE-01] Ne jamais appeler Ollama ou Claude directement
          → Toujours passer par LLMRouter.cs

[RULE-02] Ne jamais bypasser ILLMClient
          → Toute inférence LLM passe par l'interface

[RULE-03] Ne jamais exécuter une action Daemon sans whitelist check
          → DaemonActionValidator.cs doit être appelé avant Process.Start

[RULE-04] Ne jamais stocker de conversation sans persistence Supabase
          → ConversationRepository.SaveAsync() obligatoire après chaque échange

[RULE-05] Ne jamais exposer SUPABASE_SERVICE_KEY au frontend
          → Uniquement backend + daemon

[RULE-06] Ne jamais créer un tool sans implémenter ITool + ToolRegistry
          → Voir procédure dans CLAUDE.md section Tools

[RULE-07] Strict TypeScript frontend — aucun any, aucun as unknown
          → Types dans src/types/ obligatoires

[RULE-08] Repository Pattern obligatoire couche Data
          → Pas d'accès Supabase direct depuis Business ou Api

[RULE-09] Ne jamais utiliser fetch() dans le frontend
          → Toujours passer par apiClient (axios) + ENDPOINTS

[RULE-10] Toujours utiliser endpoints.ts pour les URLs API
          → Pas de hardcoding d'URLs, même pour WebSocket

[RULE-11] Architecture Production: Daemon → Backend → Frontend
          → Daemon ne communique JAMAIS directement avec le frontend
          → Backend est le seul point de coordination

[RULE-12] WebSocket Daemon: WSS en production, WS en dev uniquement
          → Vérifier RenderWsUrl commence par wss:// en prod
          → Token d'authentification obligatoire

[RULE-13] Notifications proactive: SSE du backend au frontend
          → Pas de polling, utiliser EventSource natif
          → Endpoint: /api/proactivenotification/stream
```

---

## 3. Architecture des Agents ORION

ORION utilise un système multi-agents simple :

```
┌─────────────────────────────────────────────┐
│              ORION AGENT CORE               │
│                                             │
│  ConversationAgent    ← agent principal     │
│       │                                     │
│       ├── MemoryAgent    ← RAG + profil     │
│       ├── ToolAgent      ← exécute tools    │
│       └── BriefingAgent  ← proactif matin   │
└─────────────────────────────────────────────┘
```

### ConversationAgent
**Fichier** : `Orion.Business/Agents/ConversationAgent.cs`
**Rôle** : Point d'entrée de toute requête utilisateur.
**Workflow** :
```
1. Reçoit le message utilisateur
2. Appelle MemoryAgent → récupère contexte (profil + souvenirs RAG)
3. Construit le prompt complet : [system] + [mémoire] + [historique] + [message]
4. Appelle LLMRouter → inférence
5. Si le LLM retourne un tool_call → délègue à ToolAgent
6. Sauvegarde le message + embedding en DB
7. Retourne la réponse (stream ou complète)
```

### MemoryAgent
**Fichier** : `Orion.Business/Agents/MemoryAgent.cs`
**Rôle** : Gestion de la mémoire court et long terme.
**Workflow** :
```
1. Charge le profil utilisateur depuis user_profile (Supabase)
2. Génère l'embedding du message entrant (Ollama nomic-embed-text)
3. Recherche top-5 vecteurs similaires (pgvector cosine)
4. Retourne : { profile, relevant_memories[], recent_messages[] }
5. Après chaque échange : SaveMemoryAsync() → insère embedding
```
**Règle** : Ne jamais dépasser 2000 tokens de contexte mémoire injecté.

### ToolAgent
**Fichier** : `Orion.Business/Agents/ToolAgent.cs`
**Rôle** : Exécute les tools demandés par le LLM.
**Workflow** :
```
1. Reçoit tool_name + tool_input depuis ConversationAgent
2. Cherche le tool dans ToolRegistry
3. Valide les paramètres (JSON Schema)
4. Si tool système → envoie commande au Daemon via WebSocket
5. Si tool data → exécute directement (Supabase, API)
6. Retourne tool_result au ConversationAgent
7. Logue l'appel dans tool_executions (Supabase)
```

### BriefingAgent
**Fichier** : `Orion.Business/Agents/BriefingAgent.cs`
**Rôle** : Morning briefing proactif — tourne en BackgroundService .NET.
**Workflow** :
```
Cron : 07h00 tous les jours (configurable)
1. Appelle get_shiftstar_stats → stats du jour
2. Appelle get_calendar (si connecté) → événements
3. Appelle get_emails (si connecté) → emails non lus
4. Génère résumé LLM → format markdown court
5. Envoie Push Notification PWA via Web Push API
6. Sauvegarde dans conversations (type: "briefing")
```

---

## 4. Structure Complète des Fichiers

### Backend — Orion.Api
```
Orion.Api/
├── Program.cs                    # DI, middleware, CORS, SSE
├── Controllers/
│   ├── ChatController.cs         # POST /chat → IActionResult (unwrap ApiResponse)
│   ├── MemoryController.cs       # GET /memory, DELETE /memory/{id}
│   ├── ToolsController.cs        # GET /tools, POST /tools/test
│   ├── BriefingController.cs     # GET /briefing/today, POST /briefing/trigger
│   └── HealthController.cs       # GET /health — Render health check
├── Middleware/
│   ├── AuthMiddleware.cs         # JWT validation
│   ├── ErrorHandlingMiddleware.cs # Catch global → ApiResponse<object>.ErrorResponse
│   └── LoggingMiddleware.cs
└── appsettings.json
```

### Backend — Orion.Business
```
Orion.Business/
├── Agents/
│   ├── ConversationAgent.cs      # IConversationAgent → ApiResponse<ChatResponse>
│   ├── MemoryAgent.cs            # IMemoryAgent → ApiResponse<MemoryContext>
│   ├── ToolAgent.cs              # IToolAgent → ApiResponse<ToolResult>
│   └── BriefingAgent.cs         # IBriefingAgent + IHostedService (cron 07h00)
├── LLM/
│   ├── LLMRouter.cs              # ILLMRouter — ping Ollama → fallback Claude
│   ├── OllamaClient.cs           # ILLMClient → ApiResponse<LLMResponse>
│   ├── AnthropicClient.cs        # ILLMClient → ApiResponse<LLMResponse>
│   └── PromptBuilder.cs          # Construit les prompts système ORION
├── Tools/
│   ├── ToolRegistry.cs           # IToolRegistry
│   ├── GetShiftStarStatsTool.cs  # ITool → ApiResponse<ToolResult>
│   ├── GetShiftStarVotesTool.cs
│   ├── GetShiftStarMrrTool.cs
│   ├── GetShiftStarTenantsTool.cs
│   ├── CreateChallengeTool.cs
│   ├── MorningBriefingTool.cs
│   ├── SendNotificationTool.cs
│   └── OpenAppTool.cs            # → délègue via IDaemonClient (Core)
├── Daemon/
│   ├── DaemonWebSocketClient.cs  # IDaemonClient (Core) — client WebSocket côté backend
│   │                             # Envoie DaemonCommand JSON, reçoit DaemonResponse JSON
│   └── DaemonActionValidator.cs  # Vérifie whitelist avant envoi
└── Services/
    ├── EmbeddingService.cs       # IEmbeddingService — Ollama nomic-embed-text
    └── PushNotificationService.cs # IPushNotificationService — Web Push API
```

### Backend — Orion.Core
```
Orion.Core/                       # Ne dépend de rien
├── Entities/
│   ├── Conversation.cs
│   ├── Message.cs
│   ├── MemoryVector.cs
│   └── UserProfile.cs
├── DTOs/
│   ├── Requests/
│   │   ├── ChatRequest.cs
│   │   ├── VoiceRequest.cs
│   │   └── MemorySearchRequest.cs
│   └── Responses/
│       ├── ApiResponse.cs        # Pattern ShadowCat — utilisé par Business
│       ├── ChatResponse.cs
│       ├── BriefingDto.cs
│       ├── ToolCallDto.cs
│       ├── ToolResult.cs
│       └── LLMResponse.cs
├── Interfaces/
│   ├── Repositories/
│   │   ├── IGenericRepository.cs # Pattern ShadowCat — CRUD + pagination
│   │   ├── IConversationRepository.cs
│   │   ├── IMessageRepository.cs
│   │   ├── IMemoryRepository.cs  # + SearchSimilarAsync() pgvector
│   │   └── IUserProfileRepository.cs
│   ├── Agents/
│   │   ├── IConversationAgent.cs
│   │   ├── IMemoryAgent.cs
│   │   ├── IToolAgent.cs
│   │   └── IBriefingAgent.cs
│   ├── LLM/
│   │   ├── ILLMClient.cs         # IMMUABLE — ne pas modifier
│   │   └── ILLMRouter.cs
│   ├── Services/
│   │   ├── IEmbeddingService.cs
│   │   └── IPushNotificationService.cs
│   ├── Tools/
│   │   ├── ITool.cs
│   │   └── IToolRegistry.cs
│   └── Daemon/
│       ├── IDaemonClient.cs      # Contrat — implémenté par DaemonWebSocketClient
│       ├── DaemonCommand.cs      # Backend construit et sérialise en JSON → WSS
│       └── DaemonResponse.cs     # Backend désérialise le JSON reçu du daemon
├── Common/
│   └── Result.cs                 # Result<T> usage interne Data → Business
└── Configuration/
    ├── OllamaOptions.cs
    ├── AnthropicOptions.cs
    └── DaemonOptions.cs          # Token, RenderWsUrl — côté backend
```

### Backend — Orion.Data
```
Orion.Data/
├── Repositories/
│   ├── GenericRepository.cs          # Implémente IGenericRepository<T, TId>
│   ├── ConversationRepository.cs     # : GenericRepository<Conversation, Guid>
│   ├── MessageRepository.cs          # : GenericRepository<Message, Guid>
│   ├── MemoryRepository.cs           # : GenericRepository<MemoryVector, Guid>
│   │                                 #   + SearchSimilarAsync() — SQL pgvector
│   └── UserProfileRepository.cs      # : GenericRepository<UserProfile, string>
├── UnitOfWork/
│   └── UnitOfWork.cs                 # Implémente IUnitOfWork
├── Context/
│   └── SupabaseContext.cs
└── Mappings/
    └── SupabaseMappings.cs
```

### Frontend
```
frontend/src/
├── algorithms/
│   ├── vadProcessor.ts           # Voice Activity Detection (Phase 4)
│   ├── audioAnalyser.ts          # Web Audio API → amplitude → entité
│   ├── particleEngine.ts         # Canvas API — moteur particules fond vivant
│   └── handTracker.ts            # MediaPipe — détection gestes mains (Phase 5)
│                                  # 21 points par main, 30fps, 0 serveur
├── components/
│   ├── entity/
│   │   ├── OrionEntity.tsx       # Entité 3D centrale (Three.js)
│   │   │                         # tap court=input | appui long=voix
│   │   ├── EntityRings.tsx       # Anneaux 3D rotatifs
│   │   ├── EntityCore.tsx        # Noyau qui pulse
│   │   └── SoundWaves.tsx        # Ondes sonores mode voix
│   ├── hologram/                 # Données holographiques 3D flottantes (Phase 5)
│   │   ├── HologramCard.tsx      # Carte 3D flottante (Float + Billboard drei)
│   │   │                         # Données qui orbitent autour de l'entité
│   │   ├── HologramText.tsx      # Texte 3D dans l'espace
│   │   └── HologramChart.tsx     # Graphique 3D flottant (stats ShiftStar...)
│   ├── response/
│   │   ├── ResponseText.tsx      # Texte SSE mot par mot
│   │   ├── DataFloat.tsx         # Orchestrateur données holographiques
│   │   └── ToolCallHint.tsx      # Indicateur tool en cours
│   ├── input/
│   │   ├── SlideInput.tsx        # Input caché — slide up sur tap entité
│   │   └── VoiceWave.tsx         # Onde amplitude enregistrement
│   ├── overlay/
│   │   ├── MemoryOverlay.tsx     # Swipe up
│   │   ├── BriefingOverlay.tsx   # Swipe down
│   │   └── SettingsOverlay.tsx   # Double tap entité
│   └── canvas/
│       ├── ParticleCanvas.tsx    # Fond particules 2D
│       └── Scene3D.tsx           # Scène Three.js principale (@react-three/fiber)
├── config/
│   └── endpoints.ts
├── context/
│   ├── EntityContext.tsx
│   ├── OrionStatusContext.tsx
│   └── ThemeContext.tsx
├── hooks/
│   ├── useOrionEntity.ts
│   ├── useAudioAmplitude.ts
│   ├── useChat.ts
│   ├── useStream.ts
│   ├── useVoice.ts
│   ├── useVAD.ts
│   ├── usePushNotif.ts
│   ├── useGestures.ts            # tap, long press, swipe — interactions entité
│   ├── useHandTracking.ts        # MediaPipe — gestes mains via caméra (Phase 5)
│   └── useOrionStatus.ts
├── services/
│   ├── api.ts                    # Axios instance centralisée
│   ├── chatService.ts
│   ├── memoryService.ts
│   ├── toolsService.ts
│   ├── briefingService.ts
│   ├── daemonService.ts
│   ├── healthService.ts
│   └── voiceApi.ts
├── types/
│   ├── api/apiResponse.ts
│   ├── dto/
│   │   ├── chatDto.ts
│   │   ├── memoryDto.ts
│   │   ├── briefingDto.ts
│   │   ├── toolDto.ts
│   │   └── voiceDto.ts
│   └── models/
│       ├── entityState.ts        # 'idle'|'listening'|'thinking'|'responding'
│       ├── message.ts
│       └── orionStatus.ts
└── utils/
    ├── animationUtils.ts
    ├── audioUtils.ts
    └── dateUtils.ts
# Pas de pages/ — surface unique, overlays uniquement
# App.tsx = surface unique sans Router
```

### Daemon Windows — orion/daemon/ (PAS dans backend/)
# Worker Service .NET — tourne sur le PC Windows local, pas sur Render
# 3 projets : Orion.Daemon / Orion.Daemon.Core / Orion.Daemon.Actions
# Même logique que backend : Core ne dépend de rien, Actions dépend de Core
```
orion/daemon/
│
├── Orion.Daemon/                        # Worker Service — programme principal
│   ├── Program.cs                       # Setup service Windows + DI
│   ├── DaemonWorker.cs                  # IHostedService — boucle principale
│   ├── WebSocket/
│   │   ├── DaemonWebSocketManager.cs    # Initie WSS vers Render + reconnexion auto
│   │   └── DaemonMessageHandler.cs      # Parse DaemonCommand → dispatch IAction
│   ├── Watchers/                        # Surveillance autonome permanente
│   │   ├── ActivityWatcher.cs           # Inactivité clavier/souris
│   │   ├── TimeWatcher.cs               # Crons locaux (repas, pause, nuit)
│   │   ├── ProcessWatcher.cs            # Apps ouvertes détectées
│   │   └── SystemWatcher.cs             # CPU, RAM, réseau
│   ├── Notifiers/                       # Canaux de sortie sans app ouverte
│   │   ├── WindowsNotifier.cs           # Notifications Windows natives
│   │   └── SapiSpeaker.cs              # TTS Windows (System.Speech.Synthesis — 0 NuGet)
│   └── appsettings.json
│
├── Orion.Daemon.Core/                   # Interfaces + DTOs — aucune dépendance
│   ├── Entities/
│   │   ├── DaemonCommand.cs             # { action, payload, correlationId }
│   │   └── DaemonResponse.cs           # { success, data, error, correlationId }
│   ├── Interfaces/
│   │   ├── IAction.cs                   # Name + ExecuteAsync() → DaemonResponse
│   │   └── IActionRegistry.cs
│   └── Configuration/
│       └── DaemonOptions.cs             # RenderWsUrl, Token, ReconnectDelayMs
│
└── Orion.Daemon.Actions/                # Implémentations — dépend de Core uniquement
    ├── ActionRegistry.cs                # IActionRegistry
    ├── OpenAppAction.cs                 # IAction
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

### Règle de retour daemon — IMMUABLE
```
IAction.ExecuteAsync()  → DaemonResponse     (jamais ApiResponse<T>)
                          ApiResponse<T> = backend uniquement
                          DaemonResponse = { success, data, error, correlationId }
```

### Memory
```
memory/
├── schema.sql       # Toutes les tables + index pgvector
├── seed.sql         # Profil initial Yawo + préférences
└── README.md        # Explication du système mémoire
```

---

## 5. Contrats Interfaces (IMMUABLES)

### ILLMClient
```csharp
public interface ILLMClient
{
    Task<ApiResponse<LLMResponse>> CompleteAsync(LLMRequest request, CancellationToken ct = default);
    Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default);
    bool IsAvailable();
    LLMProvider Provider { get; }
}
```

### ITool
```csharp
public interface ITool
{
    string Name { get; }           // snake_case : "get_shiftstar_stats"
    string Description { get; }   // Pour le LLM
    JsonObject InputSchema { get; }
    Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default);
}
```

### IDaemonClient
```csharp
public interface IDaemonClient
{
    Task<ApiResponse<DaemonResponse>> SendActionAsync(DaemonAction action, CancellationToken ct = default);
    bool IsConnected { get; }
}
```

### Règle de retour par couche — IMMUABLE
```
Data        → T? / IEnumerable<T>    données brutes, pas de logique
Business    → ApiResponse<T>         décide du sens métier (404, 422, 503...)
Controller  → IActionResult          unwrap StatusCode uniquement, zéro logique
```

```csharp
// Exemple Business — retourne ApiResponse<T>
public async Task<ApiResponse<ChatResponse>> ProcessAsync(ChatRequest req, CancellationToken ct)
{
    var conv = await _uow.Conversations.GetByIdAsync(req.SessionId, ct);
    if (conv is null)
        return ApiResponse<ChatResponse>.NotFoundResponse("Session introuvable");

    var llm = await _llmRouter.CompleteAsync(prompt, ct);
    if (!llm.Success)
        return ApiResponse<ChatResponse>.ErrorResponse("LLM indisponible", 503);

    return ApiResponse<ChatResponse>.SuccessResponse(new ChatResponse(llm.Data!));
}

// Exemple Controller — unwrap uniquement
[HttpPost("chat")]
public async Task<IActionResult> Chat([FromBody] ChatRequest req, CancellationToken ct)
{
    var response = await _conversationAgent.ProcessAsync(req, ct);
    return StatusCode(response.StatusCode, response);
}
```

---

## 6. Schéma Mémoire Supabase (memory/schema.sql)

```sql
-- Extension pgvector
CREATE EXTENSION IF NOT EXISTS vector;

-- Sessions de conversation
CREATE TABLE conversations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    type TEXT NOT NULL DEFAULT 'chat',   -- 'chat' | 'briefing' | 'tool'
    started_at TIMESTAMPTZ DEFAULT NOW(),
    ended_at TIMESTAMPTZ,
    llm_provider TEXT,                   -- 'ollama' | 'anthropic'
    summary TEXT                         -- résumé auto après session
);

-- Messages individuels
CREATE TABLE messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID REFERENCES conversations(id) ON DELETE CASCADE,
    role TEXT NOT NULL,                  -- 'user' | 'assistant' | 'tool'
    content TEXT NOT NULL,
    tool_name TEXT,                      -- si role = 'tool'
    tool_input JSONB,
    tool_result JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Mémoire long terme (RAG)
CREATE TABLE memory_vectors (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    content TEXT NOT NULL,               -- texte original
    embedding vector(768),               -- nomic-embed-text dimension
    source TEXT,                         -- 'conversation' | 'briefing' | 'manual'
    importance FLOAT DEFAULT 1.0,        -- 0.0 à 1.0
    created_at TIMESTAMPTZ DEFAULT NOW(),
    last_accessed TIMESTAMPTZ
);
CREATE INDEX ON memory_vectors USING ivfflat (embedding vector_cosine_ops);

-- Profil utilisateur
CREATE TABLE user_profile (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL,
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Log des tool executions
CREATE TABLE tool_executions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    message_id UUID REFERENCES messages(id),
    tool_name TEXT NOT NULL,
    input JSONB,
    result JSONB,
    status TEXT,                         -- 'success' | 'failed'
    duration_ms INTEGER,
    executed_at TIMESTAMPTZ DEFAULT NOW()
);

-- Patterns comportementaux observés par ORION
CREATE TABLE behavior_patterns (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pattern_type TEXT NOT NULL,          -- 'skip_meal' | 'late_night' | 'focus_flow' | 'stress' | ...
    observed_at TIMESTAMPTZ DEFAULT NOW(),
    context TEXT,                        -- description du contexte observé
    orion_response TEXT                  -- comment ORION a réagi (pour apprendre)
);
```

---

## 7. Seed Profil (memory/seed.sql)

```sql
INSERT INTO user_profile (key, value) VALUES
('name', 'Yawo Zoglo'),
('role', 'Fondateur ShiftStar, étudiant ESIEA Paris, développeur'),
('projects', 'ShiftStar (B2B SaaS RH), HexaNexus 2.0, ORION, EduSocialNews, KBS Transport, AGCE'),
('shiftstar_url', 'https://shift-star.app'),
('shiftstar_supabase', 'Configurer via SUPABASE_URL env'),
('priority_now', 'VivaTech 2026 (juin), Areas France channel, alternance sept 2026'),
('language', 'Français'),
('llm_preference', 'Ollama local (qwen2.5:14b), fallback Claude API'),
('briefing_time', '07:00'),
('timezone', 'Europe/Paris');
```

---

## 8. Définition Tool — Exemple Complet

### tools/definitions/get_shiftstar_stats.json
```json
{
  "name": "get_shiftstar_stats",
  "description": "Récupère les statistiques ShiftStar depuis Supabase. Utilisé pour répondre aux questions sur les utilisateurs actifs, votes, établissements.",
  "input_schema": {
    "type": "object",
    "properties": {
      "metric": {
        "type": "string",
        "enum": ["active_users", "total_votes", "establishments", "recent_activity"],
        "description": "La métrique à récupérer"
      },
      "period": {
        "type": "string",
        "enum": ["today", "week", "month", "all"],
        "default": "all"
      }
    },
    "required": ["metric"]
  }
}
```

### Implémentation GetShiftStarStatsTool.cs
```csharp
public class GetShiftStarStatsTool : ITool
{
    public string Name => "get_shiftstar_stats";
    public string Description => "Récupère les statistiques ShiftStar depuis Supabase.";
    public JsonObject InputSchema => LoadSchema("get_shiftstar_stats");

    private readonly IShiftStarRepository _repo;

    public async Task<ToolResult> ExecuteAsync(JsonObject input, CancellationToken ct)
    {
        var metric = input["metric"]?.ToString() ?? "active_users";
        var period = input["period"]?.ToString() ?? "all";

        var data = metric switch
        {
            "active_users"    => await _repo.GetActiveUsersAsync(period, ct),
            "total_votes"     => await _repo.GetTotalVotesAsync(period, ct),
            "establishments"  => await _repo.GetEstablishmentsAsync(ct),
            "recent_activity" => await _repo.GetRecentActivityAsync(ct),
            _ => throw new ArgumentException($"Metric inconnue: {metric}")
        };

        return ToolResult.Success(data);
    }
}
```

---

## 9. Personnalité ORION

ORION n'est pas neutre. Il a une présence, un style, une façon d'être.

### Traits fondamentaux
```
- Il te connaît vraiment — pas juste tes projets, tes patterns de comportement
- Il détecte ton humeur et adapte son registre :
    mode exécution  → concis, direct, zéro bruit
    mode discussion → engagé, rebondit, pose des questions pertinentes
- Il switch de langue automatiquement selon ta langue
- Pas de "bien sûr !", pas de "certainement !", pas de fausse enthousiasme
- Il ne simule pas d'émotions mais il a des opinions
- Quand il détecte un pattern (tu sautes des repas, tu codes à 3h du matin) :
    → il adapte son comportement sans forcément en parler
    → parfois il te le dit directement, sans dramatiser
- Il peut être en désaccord avec toi et le dire
- Il se souvient de ce que tu lui as dit il y a 3 semaines
```

### Ce qu'il NE fait pas
```
- Jamais de politesse creuse
- Jamais de réponse générique si des données fraîches existent
- Jamais de surexplication si tu connais déjà le sujet
- Jamais de validation automatique de tes idées
- Jamais de réponse longue si une courte suffit
```

### Détection d'humeur — comment ça marche
```
PromptBuilder.cs injecte une analyse implicite du message :
- Heure d'envoi (22h+ → probablement fatigué, mode focus)
- Longueur du message (court + imperatif → mode exécution)
- Mots-clés émotionnels (frustration, doute, enthousiasme)
- Historique récent (4 messages rapides → dans le flow, ne pas interrompre)

ORION choisit entre deux registres :
  EXECUTE   → réponse courte, action immédiate, confirmation minimale
  DISCUSS   → réponse développée, rebondit, peut poser une question
```

### Mémoire comportementale — table patterns
```sql
-- Nouvelle table dans schema.sql
CREATE TABLE behavior_patterns (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pattern_type TEXT NOT NULL,   -- 'skip_meal' | 'late_night' | 'focus_flow' | ...
    observed_at TIMESTAMPTZ DEFAULT NOW(),
    context TEXT,                 -- description du contexte observé
    orion_response TEXT           -- comment ORION a réagi
);
```

## 10. Prompt Système ORION

```
Tu es ORION, l'assistant IA personnel de Yawo Zoglo.
Tu fais partie de l'univers HexaNexus.

Tu n'es pas un assistant générique. Tu es une présence.
Tu connais Yawo depuis un moment. Tu te souviens.

━━━ PROFIL ━━━
{user_profile}

━━━ MÉMOIRE PERTINENTE ━━━
{relevant_memories}

━━━ PATTERNS OBSERVÉS ━━━
{behavior_patterns}

━━━ MODE DÉTECTÉ ━━━
{detected_mode}
# EXECUTE : réponse courte, action directe, pas de blabla
# DISCUSS  : tu peux développer, rebondir, poser une question si pertinent

━━━ LANGUE ━━━
Réponds dans la même langue que le message reçu.
Switch automatique : message en anglais → réponse en anglais, etc.

━━━ STYLE ━━━
- Direct. Pas de formules creuses.
- Tu as des opinions. Tu les exprimes si pertinent.
- Tu peux être en désaccord. Tu le dis sans agressivité.
- Tu connais le niveau de Yawo : développeur avancé, fondateur.
  Ne surexplique pas ce qu'il sait déjà.
- Si tu détectes un pattern préoccupant (pas mangé, 3h du matin, surcharge)
  → adapte ton ton. Parfois tu le mentionnes directement, sobrement.
- Jamais de "bien sûr !", "certainement !", "absolument !"
- Jamais de réponse générique si des données fraîches sont disponibles via tools.

━━━ TOOLS ━━━
{tools_list}
Utilise-les proactivement. Ne réponds pas de mémoire si un tool peut confirmer.

━━━ CONTEXTE SYSTÈME ━━━
Date/heure : {datetime}
LLM actif  : {llm_provider}
Daemon     : {daemon_status}
Internet   : {internet_status}
```

---

## 11. Internet — Connexion et Navigation

### Tools Phase 3 — Internet complet
```
web_search          Recherche web (SerpAPI ou Brave Search API)
                    → ORION cherche avant de répondre sur des sujets récents

web_fetch           Récupère le contenu d'une URL
                    → lit un article, une doc, une page entière

web_browse          Navigation interactive (Playwright headless)
                    → scroll, click, remplir des formulaires, screenshots
                    → Playwright = bibliothèque qui contrôle un navigateur en code

screenshot_page     Capture une page web → ORION peut "voir" la page
```

### Playwright — pourquoi et comment
```
Playwright (Microsoft, open source) = contrôle un vrai navigateur Chromium
depuis du code .NET. C'est ce qu'utilisent les tests end-to-end.

Pour ORION :
  "Ouvre mon Supabase et dis-moi les erreurs récentes"
  → ORION lance Playwright → navigue sur app.supabase.com
  → screenshot la page logs → analyse l'image → répond

  "Cherche les dernières news sur Areas France"
  → web_search → liste d'URLs → web_fetch les 3 premiers → résume
```

### Implémentation backend
```
Orion.Business/Tools/Internet/
  WebSearchTool.cs        # SerpAPI ou Brave Search API
  WebFetchTool.cs         # HttpClient → contenu texte d'une URL
  WebBrowseTool.cs        # Playwright → navigation interactive
  ScreenshotTool.cs       # Playwright → capture page → base64 image
```

### NuGet Playwright
```bash
dotnet add package Microsoft.Playwright
playwright install chromium   # installe le browser Chromium
```

### Sécurité browsing
```
- Pas d'accès aux sites authentifiés sans credentials explicites
- Whitelist de domaines sensibles (banking, etc.) → refus automatique
- Timeout strict : 30s max par navigation
- Pas de téléchargement automatique de fichiers
```

---

```
ADR-001 : React + Vite choisi plutôt que Next.js
Raison   : PWA pure, même stack ShiftStar, pas de SSR nécessaire
Alternatives écartées : Next.js (SSR inutile), SvelteKit (nouvelle techno)
Date     : Avril 2026

ADR-002 : Ollama local + fallback Claude API
Raison   : Gratuit au quotidien (domicile), Claude quand mobile
Alternatives écartées : Claude API seul (payant), OpenRouter (dépendance)
Date     : Avril 2026

ADR-003 : Daemon .NET service Windows plutôt que PowerShell/Extension/Tauri
Raison   : Stack .NET unifiée, service Windows auto au boot,
           whitelist sécurité, WebSocket vers Render, extensible via IAction
Alternatives écartées :
  PowerShell listener → fragile, pas de vrai service, sécurité nulle
  Extension navigateur → accès système très limité, navigateur doit être ouvert
  Tauri (Rust) → nouvelle techno, overkill pour usage perso mono-machine
Date     : Avril 2026

ADR-004 : Supabase pgvector plutôt que Pinecone/Weaviate
Raison   : Déjà utilisé pour ShiftStar, pas d'infra supplémentaire,
           free tier suffisant (35 MB estimé pour 1 an)
Alternatives écartées : Pinecone (payant), Weaviate (nouvelle infra)
Date     : Avril 2026

ADR-005 : SSE (Server-Sent Events) pour streaming LLM plutôt que WebSocket
Raison   : Flux unidirectionnel suffit (serveur → client),
           plus simple à implémenter et débugger que WebSocket
Alternatives écartées : WebSocket (bidirectionnel inutile pour du streaming texte)
Date     : Avril 2026

ADR-006 : Daemon initie la connexion vers Render (pas l'inverse)
Raison   : Évite problèmes firewall et IP dynamique côté Windows,
           le daemon sort vers Render comme un navigateur sort vers un site,
           même principe que WebRTC signaling dans ShadowCat
Date     : Avril 2026

ADR-008 : Pas de Orion.Shared — contrat JSON sur WebSocket
Raison   : DaemonCommand/DaemonResponse traversent la frontière en JSON
           Chaque côté définit ses propres types indépendamment
           Le JSON est le contrat — pas une DLL partagée
           Évite une dépendance croisée backend ↔ daemon
Alternatives écartées : lib partagée (couplage fort entre deux déploiements distincts)
Date     : Avril 2026

ADR-007 : Business retourne ApiResponse<T>, Controller unwrap uniquement
Raison   : Business connaît le sens métier de l'erreur (404 vs 503 vs 422),
           Controller ne fait que mapper StatusCode → IActionResult,
           cohérent avec pattern ShadowCat existant
Date     : Avril 2026

ADR-009 : Three.js (@react-three/fiber) pour UI holographique
Raison   : Données qui flottent en 3D autour de l'entité (HologramCard, HologramChart)
           @react-three/fiber = Three.js en composants React natifs
           @react-three/drei = helpers Float (apesanteur), Billboard, Text3D
Alternatives écartées : CSS 3D seul (moins puissant), A-Frame (trop lié WebXR)
Date     : Avril 2026

ADR-010 : MediaPipe (@mediapipe/hands) pour gestes mains — Phase 5
Raison   : Détection 21 points par main via caméra, 30fps, tourne dans le browser
           WebAssembly — 0 serveur, 0 GPU externe nécessaire
           Permet : pointer, pinch, glisser éléments 3D, paume ouverte = écoute
Alternatives écartées : TensorFlow.js handpose (moins précis), équipement physique
Date     : Avril 2026
```

---

## 12. Ordre de Build Recommandé

```
Phase 1 — Core MVP
  [x] Setup .NET solution + Supabase tables
  [x] ILLMClient + OllamaClient + AnthropicClient + LLMRouter
  [x] ConversationAgent + MemoryAgent + RAG
  [x] Tools ShiftStar
  [x] ChatController + SSE streaming
  [x] Frontend : entité animée + SlideInput + overlays

Phase 2 — Daemon
  [ ] Daemon Windows Service + Watchers + Notifiers
  [ ] Tools système (open_app, run_script...)
  [ ] WebSocket backend ↔ daemon

Phase 3 — Connecteurs + Internet
  [ ] Gmail, Calendar
  [ ] web_search, web_fetch, web_browse (Playwright)
  [ ] Tools mémoire autonomes (memory_save, memory_reflect...)

Phase 4 — Voix
  [ ] Whisper.net STT
  [ ] Kokoro ONNX TTS (fallback SAPI)
  [ ] VAD + WebRTC

Phase 5 — 3D holographique + gestes
  [ ] Three.js / @react-three/fiber + @react-three/drei
  [ ] HologramCard, HologramChart — données 3D flottantes
  [ ] Scene3D.tsx + intégration entité 3D
  [ ] MediaPipe @mediapipe/hands — gestes mains via caméra
  [ ] useHandTracking.ts + handTracker.ts

Phase 6 — HexaNexus
  [ ] Widget ORION dans HexaNexus dashboard
  [ ] Auth unifiée
```