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
├── Program.cs                    # DI, middleware, CORS, WebSocket
├── Controllers/
│   ├── ChatController.cs         # POST /chat, GET /chat/stream (SSE)
│   ├── MemoryController.cs       # GET /memory, DELETE /memory/{id}
│   ├── ToolsController.cs        # GET /tools (liste), POST /tools/test
│   ├── BriefingController.cs     # GET /briefing/today, POST /briefing/trigger
│   └── HealthController.cs       # GET /health (Render health check)
├── Middleware/
│   ├── AuthMiddleware.cs         # JWT validation
│   ├── ErrorHandlingMiddleware.cs
│   └── LoggingMiddleware.cs
├── Hubs/
│   └── OrionHub.cs               # SignalR hub (streaming réponses)
└── appsettings.json
```

### Backend — Orion.Business
```
Orion.Business/
├── Agents/
│   ├── ConversationAgent.cs
│   ├── MemoryAgent.cs
│   ├── ToolAgent.cs
│   └── BriefingAgent.cs
├── LLM/
│   ├── LLMRouter.cs              # Sélection Ollama/Claude + fallback
│   ├── OllamaClient.cs           # Implémente ILLMClient
│   ├── AnthropicClient.cs        # Implémente ILLMClient
│   └── PromptBuilder.cs          # Construit les prompts système
├── Tools/
│   ├── ToolRegistry.cs           # Registre de tous les tools
│   ├── GetShiftStarStatsTool.cs  # Phase 1
│   ├── GetShiftStarVotesTool.cs  # Phase 1
│   ├── MorningBriefingTool.cs    # Phase 1
│   ├── SendNotificationTool.cs   # Phase 1
│   ├── OpenAppTool.cs            # Phase 2 (via Daemon)
│   ├── RunScriptTool.cs          # Phase 2 (via Daemon)
│   └── GetEmailsTool.cs          # Phase 3
├── Daemon/
│   ├── DaemonWebSocketClient.cs  # Connexion backend → daemon
│   └── DaemonActionValidator.cs  # Whitelist check
└── Services/
    ├── EmbeddingService.cs       # Génère embeddings (Ollama)
    └── PushNotificationService.cs
```

### Backend — Orion.Core
```
Orion.Core/
├── Entities/
│   ├── Conversation.cs
│   ├── Message.cs
│   ├── MemoryVector.cs
│   └── UserProfile.cs
├── DTOs/
│   ├── ChatRequest.cs            # { message, sessionId }
│   ├── ChatResponse.cs           # { response, toolsCalled[], sources[] }
│   ├── ToolCallDto.cs
│   └── BriefingDto.cs
├── Interfaces/
│   ├── ILLMClient.cs             # IMMUABLE — ne pas modifier
│   ├── ITool.cs                  # Contrat de chaque tool
│   ├── IConversationRepository.cs
│   ├── IMemoryRepository.cs
│   └── IDaemonClient.cs
└── Enums/
    ├── LLMProvider.cs            # Ollama, Anthropic
    └── ToolStatus.cs             # Pending, Running, Success, Failed
```

### Backend — Orion.Data
```
Orion.Data/
├── Repositories/
│   ├── ConversationRepository.cs
│   ├── MessageRepository.cs
│   ├── MemoryRepository.cs       # pgvector queries
│   └── UserProfileRepository.cs
├── SupabaseContext.cs            # Client Supabase configuré
└── Migrations/
    └── (géré via memory/schema.sql)
```

### Frontend
```
frontend/src/
├── components/
│   ├── chat/
│   │   ├── ChatWindow.tsx        # Conteneur principal
│   │   ├── MessageBubble.tsx     # Message user / ORION
│   │   ├── StreamingText.tsx     # Texte qui s'affiche en temps réel
│   │   └── ToolCallCard.tsx      # Affiche tool appelé + résultat
│   ├── ui/
│   │   ├── StatusBar.tsx         # Mode LLM, connexion daemon
│   │   ├── VoiceButton.tsx       # Phase 2
│   │   └── MemoryPanel.tsx       # Souvenirs récents
│   └── briefing/
│       └── BriefingCard.tsx      # Morning briefing formaté
├── pages/
│   ├── Home.tsx                  # Chat principal
│   ├── Memory.tsx                # Visualisation mémoire
│   ├── Settings.tsx              # Config LLM, daemon, notifs
│   └── Briefing.tsx              # Historique briefings
├── services/
│   ├── chatService.ts            # POST /chat, stream SSE
│   ├── memoryService.ts          # GET/DELETE /memory
│   ├── toolsService.ts           # GET /tools
│   └── briefingService.ts        # GET /briefing
├── hooks/
│   ├── useChat.ts                # Gestion état conversation
│   ├── useStream.ts              # Lecture SSE stream
│   ├── useVoice.ts               # Web Speech API (Phase 2)
│   └── usePushNotif.ts           # Service Worker notifications
├── types/
│   ├── message.ts                # Message, Role, ToolCall
│   ├── memory.ts                 # MemoryVector, UserProfile
│   ├── tool.ts                   # Tool, ToolResult
│   └── briefing.ts               # Briefing, BriefingItem
├── config/
│   └── endpoints.ts              # Toutes les URLs API centralisées
└── main.tsx                      # PWA entry point + SW registration
```

### Daemon Windows
```
daemon/
├── Orion.Daemon/
│   ├── Program.cs                # Windows Service setup
│   ├── DaemonService.cs          # WebSocket listener principal
│   ├── ActionDispatcher.cs       # Route vers la bonne action
│   └── DaemonLogger.cs           # Log chaque action
├── actions/
│   ├── IAction.cs                # Contrat action
│   ├── OpenAppAction.cs          # Process.Start app
│   ├── RunScriptAction.cs        # PowerShell ExecutionPolicy
│   ├── OpenEditorAction.cs       # VS Code + fichier optionnel
│   ├── LaunchClaudeAction.cs     # Ouvre claude.ai dans browser
│   └── GetSystemStatusAction.cs  # CPU/RAM/disk info
└── daemon.config.json            # Whitelist apps + chemins
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
    Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken ct = default);
    Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default);
    bool IsAvailable();
    LLMProvider Provider { get; }
}
```

### ITool
```csharp
public interface ITool
{
    string Name { get; }                    // snake_case : "get_shiftstar_stats"
    string Description { get; }            // Pour le LLM
    JsonObject InputSchema { get; }         // JSON Schema des paramètres
    Task<ToolResult> ExecuteAsync(JsonObject input, CancellationToken ct = default);
}
```

### IDaemonClient
```csharp
public interface IDaemonClient
{
    Task<DaemonResponse> SendActionAsync(DaemonAction action, CancellationToken ct = default);
    bool IsConnected { get; }
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
('llm_preference', 'Ollama local (Kimi K2), fallback Claude API'),
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

## 9. Prompt Système ORION

```
Tu es ORION, l'assistant IA personnel de Yawo Zoglo.
Tu fais partie de l'écosystème HexaNexus.

CONTEXTE UTILISATEUR :
{user_profile}

SOUVENIRS PERTINENTS :
{relevant_memories}

RÈGLES DE COMPORTEMENT :
- Réponds toujours en français sauf si explicitement demandé autrement
- Sois direct, factuel, technique — Yawo est développeur avancé
- Pas de formules de politesse inutiles, pas de "bien sûr !", pas de "certainement !"
- Si tu as un doute sur une information → dis-le clairement
- Utilise les tools disponibles avant de répondre si la question nécessite des données fraîches
- Pour les actions système (ouvrir une app, lancer un script) → vérifie que le daemon est connecté d'abord
- Tu connais les projets de Yawo : ShiftStar, HexaNexus 2.0, ORION, EduSocialNews, KBS Transport

TOOLS DISPONIBLES :
{tools_list}

DATE ET HEURE ACTUELLES : {datetime}
MODE LLM ACTIF : {llm_provider}
DAEMON CONNECTÉ : {daemon_status}
```

---

## 10. Décisions Architecturales (ADR)

```
ADR-001 : React + Vite choisi plutôt que Next.js
Raison   : PWA pure, même stack ShiftStar, pas de SSR nécessaire
Date     : Avril 2026

ADR-002 : Ollama local + fallback Claude API
Raison   : Gratuit au quotidien (domicile), Claude quand mobile
Date     : Avril 2026

ADR-003 : Daemon .NET séparé plutôt qu'API système
Raison   : Sécurité (whitelist), isolation, service Windows natif
Date     : Avril 2026

ADR-004 : Supabase pgvector plutôt que Pinecone/Weaviate
Raison   : Déjà utilisé pour ShiftStar, pas d'infra supplémentaire
Date     : Avril 2026

ADR-005 : SSE (Server-Sent Events) pour streaming plutôt que WebSocket
Raison   : Unidirectionnel suffit pour le streaming LLM, plus simple
Date     : Avril 2026
```

---

## 11. Ordre de Build Recommandé

```
Semaine 1
  [x] Créer repo GitHub : orion/
  [x] Setup .NET solution (4 projets)
  [x] Supabase : créer tables (memory/schema.sql)
  [x] Supabase : seed profil (memory/seed.sql)
  [x] ILLMClient + OllamaClient + AnthropicClient
  [x] LLMRouter (ping Ollama → fallback)
  [x] ConversationAgent minimal (sans mémoire)
  [x] ChatController POST /chat
  [x] Test Postman : envoyer message → réponse

Semaine 2
  [x] MemoryAgent + EmbeddingService
  [x] pgvector queries (ConversationRepository)
  [x] RAG intégré dans ConversationAgent
  [x] ToolRegistry + ITool + GetShiftStarStatsTool
  [x] ToolAgent
  [x] SSE streaming (ChatController + StreamingText.tsx)

Semaine 3
  [x] Frontend PWA : ChatWindow, MessageBubble, StreamingText
  [x] manifest.json + Service Worker (installable)
  [x] StatusBar (mode LLM actif)
  [x] BriefingAgent (BackgroundService 07h00)
  [x] Push Notifications PWA

Après
  [ ] Daemon Windows Service
  [ ] Tools système (open_app, run_script...)
  [ ] Gmail + Calendar connecteurs
```
