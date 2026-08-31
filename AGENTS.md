# ORION — AGENTS.md
# Instructions pour agents IA travaillant sur ce projet

## Rôle de ce fichier
Ce fichier est lu par tout agent IA (Claude Code, Cursor, Windsurf, Copilot) avant d'intervenir sur le projet ORION.
Il définit les règles de comportement, les workflows, les contraintes, et la mémoire de décisions architecturales.

---

## 1. Contexte Projet

```
Projet   : ORION — assistant IA personnel agentique
Univers  : HexaNexus (ShiftStar, ORION, HexaNexus 2.0)
Langue   : Français (réponses ORION) / Anglais (code, commentaires)
Stack    : .NET 9, React 19 + Vite, PostgreSQL + pgvector, cascade NVIDIA NIM -> Ollama
Niveau   : Développeur avancé — pas d'explications basiques
Dépôt    : PUBLIC — aucune info personnelle, aucun secret, aucun hôte réel versionné
```

---

## 2. Règles Absolues (ne jamais violer)

```
[RULE-01] Ne jamais appeler un fournisseur LLM directement
          → Toute inférence passe par IAgentLoop (AgentLoop.cs)
          → Transport : ILLMAgentClient (LLMCascade : NIM -> Ollama local)
          → L'ORDRE du tableau de la cascade EST la politique de repli

[RULE-02] Ne jamais utiliser ILLMClient / ILLMRouter pour du nouveau code
          → Ancien chemin, SANS outils : ils ne portent pas de tool_call

[RULE-03] Ne jamais exécuter une action Daemon sans whitelist check
          → DaemonActionValidator.cs, avant tout Process.Start
          → ⚠️ La whitelist ne filtre QUE le nom de l'action, jamais ses
            arguments : un chemin ou une URL passe sans être examiné.
            Le PÉRIMÈTRE est la responsabilité de l'action elle-même.

[RULE-04] Ne jamais stocker de conversation sans persistence Supabase
          → ConversationRepository.SaveAsync() obligatoire après chaque échange

[RULE-05] Ne jamais exposer SUPABASE_SERVICE_KEY au frontend
          → Uniquement backend + daemon

[RULE-06] Ne jamais créer un tool sans implémenter ITool + l'enregistrer DEUX
          fois dans Program.cs (le type concret, PUIS ITool)
          → Sans la 2e ligne, ToolRegistry ne le découvre pas : le modèle ne
            le voit jamais, et rien n'échoue bruyamment
          → Trancher IsDeferrable, IsDestructive ET le périmètre (docs/tools.md)

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
            (le nom "Render" est un VESTIGE : l'hébergement est un VPS + Nginx)
          → Token X-Daemon-Token obligatoire, et FAIL-CLOSED : jeton absent
            côté serveur = connexion REFUSÉE, jamais ouverte

[RULE-13] Notifications proactive: SSE du backend au frontend
          → Pas de polling, utiliser EventSource natif
          → Endpoint: /api/proactivenotification/stream
          → Sérialisation camelCase obligatoire (JsonNamingPolicy.CamelCase)

[RULE-14] Voice WebSocket: anti-écho obligatoire
          → Endpoint: /ws/voice (full-duplex)
          → voiceWSResponseRef bloque Web Speech TTS pendant pipeline WS
          → Vérifier window.speechSynthesis.speaking avant trigger VAD
          → Ne jamais activer Web Speech TTS et Kokoro TTS simultanément

[RULE-15] HologramResponsePanel: pur Three.js, zéro HTML
          → Texte via drei Text (SDF), pas via drei Html
          → Shader GLSL custom pour panneau holographique
          → Pas de ReactMarkdown en 3D — stripMarkdown() vers plain text

[RULE-16] FERMÉ PAR DÉFAUT, et le défaut est le REFUS
          → FallbackPolicy exige le propriétaire : une route sans attribut est
            refusée, jamais ouverte. Les exceptions sont EXPLICITES.
          → Un secret absent REFUSE au lieu d'ouvrir (fail-closed)
          → Ne jamais poser [AllowAnonymous] au niveau d'un CONTRÔLEUR : il
            l'emporte sur tout [Authorize] d'action et ne peut pas être annulé

[RULE-17] Le garde-fou des actions vit dans le CODE, jamais dans le prompt
          → ORION lit le web : une page peut détourner le modèle, et la requête
            résultante est parfaitement AUTHENTIFIÉE. Aucun contrôle d'accès ne
            peut l'arrêter — seule une règle placée APRÈS la décision du modèle
            le peut (IToolInvoker + ITool.IsDestructive).
          → Une phrase dans un prompt système est une SUGGESTION, pas une règle

[RULE-18] Tout outil qui touche au disque, au réseau interne ou aux processus
          porte un PÉRIMÈTRE explicite, appliqué dans le code qui agit
          → Disque : Orion.Daemon.Core/Security/PathScope — Resoudre() AVANT
            tout accès, et ouvrir le chemin qu'il RETOURNE
          → Réseau : Orion.Business/Tools/Internet/UrlScope — VerifierAsync()
            AVANT la requête, et suivre les redirections À LA MAIN en
            revalidant chaque saut
          → Ne JAMAIS réécrire un de ces contrôles à côté : screenshot_page
            avait le sien, une liste de sous-chaînes qui bloquait « login » et
            laissait passer 169.254.169.254
          → Comparer sur le chemin NORMALISÉ (après GetFullPath), sinon ..\..\
            contourne le contrôle
          → Périmètre vide = rien n'est autorisé, PAS "tout est autorisé"
          → Une option de config déclarée et jamais lue est pire que son
            absence : elle se fait passer pour une défense (cf. docs/security.md)

[RULE-19] Aucun secret, aucune info personnelle, aucun hôte réel versionné
          → appsettings.json / appsettings.Development.json / .env : gitignorés
          → Le dépôt est PUBLIC. Modèle de config : .env.example
```

---

## 3. Architecture des Agents ORION

⚠️ **Corrigé le 2026-08-27.** Ce chapitre décrivait un `MemoryAgent` et un `ToolAgent` qui
**n'existent pas** dans le dépôt. La mémoire est un *service*, et l'exécution d'outil est passée
sous `IToolInvoker`. Le fichier `Orion.Business/Agents/` contient exactement trois agents.

```
┌──────────────────────────────────────────────────────────────────┐
│  AgentLoop  (IAgentLoop)          ← LE cœur : boucle multi-tours │
│      │                                                           │
│      ├── ILLMAgentClient → LLMCascade [ NIM , Ollama ]           │
│      ├── IToolInvoker    → exécute │ diffère │ refuse            │
│      └── PromptBuilder   → prompt stable → volatil (cache)       │
│                                                                  │
│  ConversationAgent  ← prépare le contexte, persiste, streame     │
│  BriefingAgent      ← briefing proactif du matin                 │
│                                                                  │
│  Services (PAS des agents) :                                     │
│    MemoryService · MemoryConsolidator · MemoryRevectorizer       │
│    ProactiveLearningService · ChatService · AuditService         │
└──────────────────────────────────────────────────────────────────┘
```

### AgentLoop — `Orion.Business/Agents/AgentLoop.cs`

**Rôle** : la boucle agentique. Le modèle peut enchaîner plusieurs outils avant de répondre.

```
1. Construit le prompt (PromptBuilder) : sections stable → volatil, pour le cache de préfixe
2. Injecte le catalogue d'outils FILTRÉ — un outil qui ne peut pas aboutir n'est pas proposé
3. Streame depuis ILLMAgentClient
4. tool_call reçu → IToolInvoker.InvokeAsync(...)   ← JAMAIS tool.ExecuteAsync en direct
5. Réinjecte le résultat, reboucle jusqu'à réponse finale ou plafond de tours
6. Émet des événements typés : token · tool_start · tool_result · done · error
```

**Règle** : aucun agent ni service n'appelle un fournisseur LLM directement (RULE-01).

### ConversationAgent — `Orion.Business/Agents/ConversationAgent.cs`

**Rôle** : point d'entrée d'une requête utilisateur — contexte, persistance, streaming.

```
PrepareStreamAsync(message, sessionId) → ApiResponse<StreamContext>
   valide la session · charge profil + souvenirs RAG · construit le prompt
StreamLLMAsync(streamContext) → IAsyncEnumerable<string>
   utilisé par VoiceWebSocketHandler et par le SSE de chat
puis : sauvegarde du message complet + embedding
```

**Règle** : toute conversation est persistée, **sans exception**. DB inaccessible → `ApiResponse`
503, le tour échoue. Une conversation perdue est pire qu'un tour raté.

### BriefingAgent — `Orion.Business/Agents/BriefingAgent.cs`

**Rôle** : briefing proactif, déclenché par `BriefingScheduler` (`BackgroundService`).
Il s'appuie sur les outils réellement disponibles (système, git, mémoire, web) — les anciens
`get_emails` / `get_calendar` / `get_shiftstar_stats` n'ont jamais existé.

### Mémoire — services, pas agent

```
MemoryService        recherche vectorielle top-K (pgvector, cosinus) + profil
MemoryConsolidator   compacte les épisodes en souvenirs durables, schéma fermé 4 slots
MemoryRevectorizer   rejoue toute la table quand le modèle d'embedding change
```

**Règles** :
- Ne jamais dépasser ~2000 tokens de contexte mémoire injecté.
- RAG **non bloquant** : embeddings en panne → liste vide, la conversation continue.
- ⚠️ Un embedding **ne bascule pas à chaud** : chaque modèle a son propre espace vectoriel.
  Changer de fournisseur impose une revectorisation complète. Modèle et dimension sont écrits
  À CÔTÉ de chaque vecteur et vérifiés au démarrage — mélanger deux espaces ne lève **aucune
  erreur** et renvoie des résultats absurdes.

---

## 4. Structure Complète des Fichiers

### Backend — Orion.Api

⚠️ **Corrigé le 2026-08-27** : `AuthMiddleware` et `LoggingMiddleware` n'existent pas.
`ToolsController` a été ajouté depuis (HUD → appel d'outil) et vit à côté de
`GET /api/daemon/tools`, qui reste.

```
Orion.Api/
├── Program.cs                              # DI, auth, middleware, CORS, WebSocket, sonde LLM
├── Controllers/
│   ├── AuthController.cs                   # POST /auth/login · /auth/stream-ticket
│   ├── ChatController.cs                   # POST /chat · /chat/stream (SSE)
│   ├── MemoryController.cs                 # GET/POST/DELETE /memory · /search · /revectorize
│   ├── DaemonController.cs                 # GET /daemon/status · /tools · POST /daemon/action
│   ├── ToolsController.cs                  # GET /tools · POST /tools/{name} — gestes du HUD,
│   │                                       # même IToolInvoker, donc même garde-fou
│   ├── DeferredActionsController.cs        # file d'actions : GET · confirm · cancel
│   ├── BriefingController.cs               # GET /briefing/today · /history
│   ├── VoiceController.cs                  # transcribe · synthesize · converse · status
│   ├── ProactiveNotificationController.cs  # SSE stream · notify · weights · trigger
│   └── HealthController.cs                 # GET /health
├── Authentication/
│   ├── OrionAuth.cs                        # UNE réponse à « qui appelle ? » — rôles, audiences
│   └── DaemonAuthenticationHandler.cs      # secret partagé → ClaimsPrincipal (fail-closed)
├── WebSockets/
│   ├── VoiceWebSocketHandler.cs            # /ws/voice full-duplex
│   └── VoiceWebSocketMiddleware.cs
├── Middleware/
│   ├── ErrorHandlingMiddleware.cs
│   └── DaemonWebSocketMiddleware.cs        # /daemon
├── Services/                               # BackgroundServices + registre SSE
│   ├── SseClientRegistry.cs                # singleton : un flux vit des heures
│   ├── BriefingScheduler.cs
│   ├── DeferredActionWatcher.cs            # draine la file au retour du daemon, expire le reste
│   └── HudBroadcastService.cs              # widgets permanents du HUD
└── appsettings.json                        # ⚠️ GITIGNORÉ — absent du dépôt
```

### Backend — Orion.Business

⚠️ **Corrigé le 2026-08-27** : `MemoryAgent`, `ToolAgent`, `LLMRouter`, `OllamaClient`,
`AnthropicClient` et les outils ShiftStar **n'existent pas** dans le dépôt.

```
Orion.Business/
├── Agents/
│   ├── AgentLoop.cs              # IAgentLoop — LA boucle multi-tours, streaming + outils
│   ├── ConversationAgent.cs      # PrepareStreamAsync → StreamContext ; StreamLLMAsync
│   └── BriefingAgent.cs          # IBriefingAgent, déclenché par BriefingScheduler
├── LLM/
│   ├── LLMCascade.cs             # ILLMAgentClient composite — L'ORDRE EST LA POLITIQUE
│   ├── NimAgentClient.cs         # NVIDIA NIM, compatible OpenAI (distant)
│   ├── OllamaAgentClient.cs      # local (repli hors-ligne)
│   └── PromptBuilder.cs          # sections stable → volatil (cache de préfixe)
├── Tools/
│   ├── ToolRegistry.cs           # IToolRegistry — auto-découverte via IEnumerable<ITool>
│   ├── ToolInvoker.cs            # IToolInvoker — POINT D'APPLICATION UNIQUE
│   ├── System/                   # 14 outils daemon (fichiers, git, processus, écran…)
│   ├── Internet/                 # web_search · web_fetch · web_browse · screenshot_page
│   └── Memory/                   # memory_save/update/forget/reflect · profile_update
├── Daemon/
│   ├── DaemonWebSocketClient.cs  # IDaemonClient
│   ├── DaemonActionValidator.cs  # whitelist des NOMS d'action (jamais des arguments)
│   └── DeferredActionService.cs  # file : confirmation, annulation, expiration
└── Services/
    ├── MemoryService.cs · MemoryConsolidator.cs · MemoryRevectorizer.cs
    ├── ChatService.cs · BriefingService.cs · AuditService.cs · HealthService.cs
    ├── OpenAiCompatibleEmbeddingService.cs        # mistral-embed, 1024 dims
    ├── TranscriptionCascade.cs                    # Voxtral → Whisper local
    └── VoxtralTranscriptionService.cs · WhisperService.cs · VoiceNotificationService.cs
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
│       ├── StreamContext.cs      # DTO pour PrepareStreamAsync → StreamLLMAsync
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
│   │   ├── IAgentLoop.cs         # LA boucle — tout passe par elle
│   │   ├── IConversationAgent.cs
│   │   └── IBriefingAgent.cs
│   ├── LLM/
│   │   ├── ILLMAgentClient.cs    # chemin ACTUEL — streaming AVEC outils
│   │   ├── ILLMClient.cs         # ancien chemin, SANS outils — ne pas réutiliser
│   │   └── ILLMRouter.cs         # idem
│   ├── Services/
│   │   ├── IEmbeddingService.cs · IWhisperService.cs · IMemoryService.cs
│   │   └── IChatService.cs · IBriefingService.cs · IAuditService.cs · IHealthService.cs
│   ├── Tools/
│   │   ├── ITool.cs
│   │   ├── IToolInvoker.cs       # point d'application UNIQUE de l'exécution
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
│   │   ├── HologramText.tsx      # Texte 3D SDF dans l'espace
│   │   ├── HologramChart.tsx     # Graphique 3D flottant
│   │   ├── HologramResponsePanel.tsx  # Panneau réponse holographique
│   │   │                              # Pure Three.js : GLSL shader, SDF Text,
│   │   │                              # particules, wireframe, anneaux orbitaux
│   │   └── index.ts              # Exports
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
│   ├── useStream.ts              # appendChunk/setStreaming pour WS + HTTP
│   ├── useVoice.ts               # LEGACY — remplacé par useVoiceWS
│   ├── useVoiceWS.ts             # Full-duplex WebSocket voice pipeline
│   ├── useVAD.ts                 # @ricky0123/vad-web + PCM streaming
│   ├── useGestures.ts            # tap, long press, swipe
│   ├── useHandTracking.ts        # MediaPipe Phase 5
│   ├── useOrionNotifications.ts  # SSE proactive notifs + Web Speech TTS
│   ├── usePushNotif.ts
│   └── useOrionStatus.ts
├── services/
│   ├── api.ts                    # Axios instance centralisée
│   ├── chatService.ts
│   ├── memoryService.ts
│   ├── toolsService.ts
│   ├── briefingService.ts
│   ├── daemonService.ts
│   ├── healthService.ts
│   ├── voiceApi.ts               # LEGACY HTTP voice
│   └── voiceWebSocket.ts         # WebSocket client /ws/voice
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
│   │   ├── WindowsToastNotifier.cs     # Toast Windows 10/11
│   │   ├── WindowsNotifier.cs           # Fallback MessageBox
│   │   ├── PowerShellTtsNotifier.cs     # TTS SAPI5 via PowerShell
│   │   └── KokoroSpeaker.cs            # TTS neuronal KokoroSharp.CPU v0.6.6
│   │                                    # Voix: ff_siwis (French female)
│   ├── ProactiveOrchestrator.cs          # Patterns → messages → SSE + TTS
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
├── seed.sql         # Profil initial utilisateur + préférences
└── README.md        # Explication du système mémoire
```

---

## 5. Contrats Interfaces (IMMUABLES)

### IAgentLoop — LE chemin, depuis 2026-08

```csharp
// Orion.Core/Interfaces/Agents/IAgentLoop.cs
// Boucle multi-tours : streaming AVEC outils. Tout passe par là.
```

### ILLMAgentClient — transport

```csharp
// Orion.Core/Interfaces/LLM/ILLMAgentClient.cs
// Implémentations : NimAgentClient · OllamaAgentClient · LLMCascade (composite)
// LLMCascade prend un ILLMAgentClient[] — L'ORDRE DU TABLEAU EST LA POLITIQUE.
// Le modèle réellement servi est élu au démarrage par ProbeAsync, en APPELANT
// le fournisseur : `ollama list` ne prouve rien.
```

⚠️ `ILLMClient` / `ILLMRouter` existent encore mais sont l'**ancien chemin, SANS outils**. Ils ne
portent pas de `tool_call`. Aucun nouveau développement ne doit les utiliser (RULE-02).

### ITool — quatre membres, trois décisions

```csharp
public interface ITool
{
    string Name { get; }            // snake_case : "get_work_context"
    string Description { get; }     // pour le LLM
    JsonObject InputSchema { get; }

    bool RequiresDaemon => false;   // passe par le PC de l'utilisateur
    bool IsDestructive  => false;   // écrit/supprime/exécute → CONFIRMATION exigée
    bool IsDeferrable   => false;   // garde un sens exécuté PLUS TARD

    Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default);

    HudCard? BuildCard(ToolResult result) => null;   // carte du HUD, ou rien
}
```

Les trois drapeaux sont **`false` par défaut** — donc le plus restrictif pour les deux premiers,
et « n'encombre pas la file » pour le troisième. Un nouvel outil daemon fait rougir
`ToolDeferrabilityTests` tant que son cas n'est pas tranché.

### IToolInvoker — le point d'application UNIQUE

```csharp
// Orion.Core/Interfaces/Tools/IToolInvoker.cs
Task<ApiResponse<ToolResult>> InvokeAsync(string toolName, JsonObject input,
                                          ToolInvocationContext context, CancellationToken ct = default);
```

**Jamais `tool.ExecuteAsync` en direct.** C'est ici, et nulle part ailleurs, que se décide :

| Situation | Issue |
|---|---|
| outil sans daemon | s'exécute |
| daemon requis + PC éteint + différable | mis en file — ORION promet et tiendra |
| daemon requis + PC éteint + non différable | **refus honnête**, jamais un faux succès |
| destructif (PC allumé compris) | confirmation exigée avant exécution |

C'est aussi le seul endroit où la carte HUD est construite : la produire ailleurs voudrait dire la
reconstruire à chaque nouveau chemin d'appel.

### IDaemonClient

```csharp
public interface IDaemonClient
{
    Task<ApiResponse<DaemonResponse>> SendActionAsync(DaemonActionRequest action, CancellationToken ct = default);
    bool IsConnected { get; }
}
```

Contrat backend ↔ daemon = **JSON sur WebSocket, pas de DLL partagée** : chaque côté définit ses
propres types. Les noms d'action ne coïncident pas toujours avec les noms d'outil (cf.
[docs/tools.md](docs/tools.md)).

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
('name', 'User'),
('role', 'Développeur'),
('projects', 'Project A, Project B'),
('priority_now', 'À configurer'),
('language', 'Français'),
('llm_preference', 'Ollama local, fallback Claude API'),
('briefing_time', '07:00'),
('timezone', 'Europe/Paris');
```

---

## 8. Définition Tool — Exemple Complet

⚠️ **Réécrit le 2026-08-27.** L'exemple précédent (`get_shiftstar_stats`) portait sur un outil qui
**n'a jamais existé**. Celui-ci est un outil réel du dépôt.

### tools/definitions/list_files.json
```json
{
  "name": "list_files",
  "description": "Liste les fichiers et dossiers d'un répertoire sur le PC Windows.",
  "input_schema": {
    "type": "object",
    "properties": {
      "path":      { "type": "string",  "description": "Chemin du répertoire à lister" },
      "pattern":   { "type": "string",  "description": "Filtre glob optionnel, ex: *.cs" },
      "recursive": { "type": "boolean", "description": "Lister récursivement (défaut: false)" }
    },
    "required": ["path"]
  }
}
```

### Orion.Business/Tools/System/ListFilesTool.cs

```csharp
public class ListFilesTool : ITool
{
    private readonly IDaemonClient _daemon;
    public ListFilesTool(IDaemonClient daemon) => _daemon = daemon;

    public string Name => "list_files";
    public string Description => "Liste les fichiers et dossiers d'un répertoire sur le PC Windows";

    // LES TROIS DÉCISIONS, explicites :
    public bool RequiresDaemon => true;    // passe par le PC
    public bool IsDestructive  => false;   // lecture seule
    public bool IsDeferrable   => false;   // « qu'y a-t-il dans ce dossier ? » ne vaut plus rien demain

    public JsonObject InputSchema => new() { /* … miroir du JSON ci-dessus … */ };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        var path = input["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path))
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre path requis", 400);

        var result = await _daemon.SendActionAsync(new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action    = "list_files",              // nom côté DAEMON, pas forcément celui de l'outil
            Payload   = new { path, pattern, recursive }
        }, ct);

        if (!result.Success)
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);

        return ApiResponse<ToolResult>.SuccessResponse(
            ToolResult.SuccessResult(JsonSerializer.Serialize(result.Data?.Data), Name));
    }
}
```

Noter ce que l'outil **ne fait pas** : il ne garde pas de `if (!_daemon.IsConnected)`. Ce garde
était recopié dans les treize outils système ; il vit désormais dans `IToolInvoker`, une fois.

### Enregistrement — DEUX lignes, pas une

```csharp
// Program.cs
builder.Services.AddScoped<ListFilesTool>();                                    // le type concret
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<ListFilesTool>()); // ← la découverte
```

`ToolRegistry` reçoit un `IEnumerable<ITool>` par injection. **Sans la seconde ligne, l'outil
existe, compile, et reste invisible au modèle** — sans qu'aucun test n'échoue.

### Côté daemon

```csharp
// daemon/Orion.Daemon.Actions/ListFilesAction.cs
public class ListFilesAction : IAction
{
    public string Name => "list_files";
    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId) { … }
}
```

Puis **ajouter le nom** à `DaemonActionValidator._allowedActions` — sinon l'endpoint direct
`/api/daemon/action` refuse l'action.

⚠️ **Et trancher le PÉRIMÈTRE** (RULE-18). `ListFilesAction` fait aujourd'hui
`Path.GetFullPath(path)` et rien d'autre : il liste **n'importe quel** répertoire de la machine.
C'est un constat ouvert de l'audit — voir [docs/security.md](docs/security.md) C1. Ne pas
reproduire ce motif dans une nouvelle action.

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
Tu es ORION, l'assistant IA personnel de l'utilisateur.
Tu fais partie de l'univers HexaNexus.

Tu n'es pas un assistant générique. Tu es une présence.
Tu connais l'utilisateur depuis un moment. Tu te souviens.

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
- Tu connais le niveau de l'utilisateur : développeur avancé, fondateur.
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

  "Cherche les dernières news sur Acme Corp"
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

⚠️ **Corrigé le 2026-08-27.** Ce bloc décrivait des contrôles comme s'ils étaient en place. Voici
l'état RÉEL, vérifié dans le code.

| Contrôle | État |
|---|---|
| Timeout strict 30 s par navigation | ✅ en place (`WebFetchTool`, `WebBrowseTool`) |
| Filtrage de domaines sensibles | ❌ **INEXISTANT** — `InternetOptions.BlockedDomains` est déclaré et **n'est lu par personne** |
| Restriction de schéma / d'hôte | ❌ **INEXISTANTE** — seul `Uri.TryCreate(..., Absolute, …)` est vérifié |
| Pas d'accès aux sites authentifiés | ⚠️ non implémenté comme règle : aucun credential n'est joint, mais rien ne l'empêche |
| Pas de téléchargement automatique | ⚠️ non vérifié |

Conséquence : `web_fetch` atteint le loopback (`http://127.0.0.1:5107/api/…`, l'API elle-même) et
les adresses de métadonnées d'instance (`169.254.169.254`). C'est le constat **E2** de
[docs/security.md](docs/security.md).

**Ces deux outils sont aussi la porte d'entrée de l'injection de prompt** (RULE-17) : ce qu'ils
rapportent entre dans le contexte du modèle et peut le détourner. Le garde-fou correspondant n'est
pas ici — il est dans `IToolInvoker`.

---

```
ADR-001 : React + Vite choisi plutôt que Next.js
Raison   : PWA pure, même stack ShiftStar, pas de SSR nécessaire
Alternatives écartées : Next.js (SSR inutile), SvelteKit (nouvelle techno)
Date     : Avril 2026

ADR-002 : Ollama local + fallback Claude API   ⛔ SUPERSÉDÉ par ADR-011
Raison   : Gratuit au quotidien (domicile), Claude quand mobile
Alternatives écartées : Claude API seul (payant), OpenRouter (dépendance)
Date     : Avril 2026
Note     : il n'existe plus aucun client Anthropic dans le dépôt.

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

ADR-006 : Daemon initie la connexion vers le backend (pas l'inverse)
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

ADR-011 : Cascade explicite NVIDIA NIM -> Ollama local  (remplace ADR-002)
Raison   : qualité en tête, survie hors-ligne en dernier. L'ORDRE DU TABLEAU
           EST LA POLITIQUE — pas de scoring implicite, pas de heuristique
           cachée : on lit Program.cs et on sait ce qui répondra.
           Même motif réutilisé pour la transcription (Voxtral -> Whisper local).
Alternatives écartées : routeur à score (illisible), fournisseur unique (pas
           de mode hors-ligne)
Date     : 2026-08 (J3)

ADR-012 : Le modèle servi est ÉLU AU DÉMARRAGE par ProbeAsync, en l'APPELANT
Raison   : `ollama list` affiche les modèles :cloud en cache local même
           retirés ou verrouillés par abonnement. Vérifié le 2026-08-20 :
           7 modèles listés, 7 inutilisables. Faire confiance à la config
           produit une panne INVISIBLE — bascule silencieuse en dégradé.
Date     : 2026-08-20

ADR-013 : Embeddings via fournisseur compatible OpenAI, Ollama RETIRÉ du
           chemin de production
Raison   : Ollama n'existe pas sur le VPS : la mémoire y serait morte en
           silence. mistral-embed, 1024 dims — mesuré en APPELANT les API
           (le catalogue ment), et indexable (pgvector plafonne à 2000).
Conséquence : un embedding NE BASCULE PAS à chaud. Modèle et dimension sont
           stockés À CÔTÉ de chaque vecteur et vérifiés au démarrage.
Date     : 2026-08-25 (J6b)

ADR-014 : IToolInvoker — point d'application UNIQUE de l'exécution d'outil
Raison   : le garde « daemon absent » était recopié dans les 13 outils
           système : 13 endroits où l'oublier. Un point unique peut décider
           d'exécuter, différer ou refuser — et refuser FRANCHEMENT plutôt
           que de rendre un « Daemon non connecté » sec qui fait paraître
           ORION cassé alors qu'il fonctionne.
Date     : 2026-08-21 (J6a)

ADR-015 : Le garde-fou des actions destructives vit dans le CODE
Raison   : il n'était qu'une phrase du prompt système — donc une SUGGESTION.
           Un modèle qui décide d'agir agit. ORION lit le web : une page peut
           le détourner, et la requête résultante est parfaitement
           AUTHENTIFIÉE — aucun contrôle d'accès ne peut l'arrêter. Seule une
           règle placée APRÈS la décision du modèle le peut.
Conséquence : toute action IsDestructive passe par la file de confirmation,
           PC allumé compris.
Date     : 2026-08-26

ADR-016 : Authentification fermée par défaut, et fail-closed
Raison   : l'API était TOTALEMENT ouverte (UseAuthorization commentée). Une
           liste de routes à protéger se périme au premier oubli ; une
           FallbackPolicy refuse ce qu'on a oublié de déclarer. De même, un
           secret absent doit REFUSER — c'est le défaut exact qui rendait le
           WebSocket daemon librement accessible quand la variable
           d'environnement manquait.
Corollaire : billet de flux à audience distincte (60 s) pour SSE/WebSocket,
           parce qu'une URL finit dans les journaux — constaté en clair dans
           access.log le 2026-08-26.
Date     : 2026-08-26

ADR-017 : Le PÉRIMÈTRE d'un outil s'applique dans le code qui agit
Raison   : audit du 2026-08-27. DaemonOptions et InternetOptions.BlockedDomains
           donnaient l'ILLUSION d'un garde-fou : injectés, jamais lus. C'est
           pire qu'une absence — ça ne se voit pas à la lecture.
Règle    : périmètre vide = rien n'autorisé. Comparaison sur le chemin
           NORMALISÉ, sinon ..\..\ contourne le contrôle.
Statut   : APPLIQUÉ le 2026-08-27, les sept constats fermes.
           Disque  -> PathScope    (Orion.Daemon.Core/Security)
           Reseau  -> UrlScope     (Orion.Business/Tools/Internet)
           DaemonOptions et InternetOptions.BlockedDomains sont enfin LUS.
           Trous residuels documentes dans docs/security.md §4.
Date     : 2026-08-27
```

---

## 12. Ordre de Build Recommandé

```
Phase 1 — Core MVP ✅   (⚠️ historique : plusieurs briques ont été REMPLACÉES depuis)
  [x] Setup .NET solution + tables PostgreSQL/pgvector
  [~] ILLMClient + LLMRouter          → remplacés par IAgentLoop + LLMCascade (ADR-011)
  [~] AnthropicClient                 → SUPPRIMÉ, n'existe plus dans le dépôt
  [~] MemoryAgent                     → devenu MemoryService (jamais un agent)
  [~] Tools ShiftStar                 → JAMAIS implémentés
  [x] ConversationAgent + RAG
  [x] ChatController + SSE streaming
  [x] Frontend : entité animée + SlideInput + overlays

Phase 2 — Daemon ✅
  [x] Daemon Windows Service + Watchers + Notifiers
  [x] Tools système (open_app, run_script...)
  [x] WebSocket backend ↔ daemon
  [x] Proactive notifications: Daemon → Backend SSE → Frontend

Phase 3 — Connecteurs + Internet
  [ ] Gmail, Calendar
  [ ] web_search, web_fetch, web_browse (Playwright)
  [ ] Tools mémoire autonomes (memory_save, memory_reflect...)

Phase 4 — Voix ✅
  [x] Whisper.net STT (local, gratuit)
  [x] KokoroSharp.CPU TTS (local, voix ff_siwis)
  [x] VAD @ricky0123/vad-web + PCM streaming
  [x] WebSocket /ws/voice full-duplex bidirectionnel
  [x] Barge-in (interrupt + CancellationToken)
  [x] Anti-écho (voiceWSResponseRef + speechSynthesis.speaking)
  [x] ConversationAgent: PrepareStreamAsync + StreamLLMAsync

Phase 5 — 3D holographique + gestes 🚧 EN COURS
  [x] Three.js / @react-three/fiber + @react-three/drei
  [x] HologramCard, HologramChart — données 3D flottantes
  [x] Scene3D.tsx + intégration dans App.tsx
  [x] HologramResponsePanel — pur Three.js (GLSL, SDF Text, particules)
  [ ] Entité ORION migrée Canvas 2D → Three.js
  [ ] MediaPipe @mediapipe/hands — gestes mains via caméra
  [ ] useHandTracking.ts + handTracker.ts

Phase 6 — HexaNexus
  [ ] Widget ORION dans HexaNexus dashboard
  [ ] Auth unifiée
```