# ORION

Assistant IA personnel **agentique**, auto-hébergé : il ne se contente pas de répondre — il
exécute des actions sur la machine de son propriétaire, garde une mémoire à long terme, et prend
la parole de lui-même quand le contexte le justifie.

Stack .NET 9 + React 19, conversation texte et **voix full-duplex**, cascade LLM distant → local
pour survivre hors-ligne.

> **Statut** : projet personnel en développement actif. L'API et les schémas bougent encore.

---

## Ce qui distingue ORION d'un client de chat

| | |
|---|---|
| **Agentique** | boucle multi-tours (`AgentLoop`) : le modèle enchaîne des outils jusqu'à aboutir, en streaming |
| **Incarné** | un daemon Windows lui donne des mains sur la machine : fichiers, git, processus, presse-papiers, écran |
| **Persistant** | mémoire vectorielle pgvector, consolidation automatique, profil utilisateur qui se met à jour seul |
| **Proactif** | des watchers observent l'activité, un scoring d'urgence décide s'il vaut la peine d'interrompre |
| **Résilient** | cascades explicites sur le LLM et la transcription : un fournisseur qui tombe dégrade, n'arrête pas |
| **Hors-ligne dégradé** | PC éteint → les actions qui gardent un sens sont mises en file et rejouées au réveil |

## Architecture

```
┌───────────────────────────────────────────────────────────────────┐
│  Frontend — React 19 + Vite (PWA, surface unique)                 │
│    Entité 3D (Three.js) · HUD à zones · VAD Silero                │
│    SSE token par token · WS /ws/voice full-duplex · anti-écho     │
├───────────────────────────────────────────────────────────────────┤
│  Backend — .NET 9, Clean Architecture                             │
│    IAgentLoop ─► ILLMAgentClient (LLMCascade)                     │
│                    ├── NVIDIA NIM   (distant, qualité)            │
│                    └── Ollama       (local, repli hors-ligne)     │
│    IToolInvoker ─► exécute │ diffère │ refuse   ← point UNIQUE    │
│    RAG : pgvector + mistral-embed (1024 dims)                     │
│    Auth : JWT propriétaire + secret partagé daemon, fermé par déf.│
├───────────────────────────────────────────────────────────────────┤
│  Daemon — .NET 9 Worker Service (Windows, session utilisateur)    │
│    WSS vers le backend (le daemon initie) · 20 actions            │
│    Watchers proactifs · TTS Kokoro ONNX + repli SAPI              │
├───────────────────────────────────────────────────────────────────┤
│  Données — PostgreSQL + pgvector (Supabase)                       │
│    conversations · messages · memory_vectors · user_profile       │
│    behavior_patterns · deferred_actions · audit_logs              │
└───────────────────────────────────────────────────────────────────┘
```

**Règle des 4 couches, immuable** : `Core` (ne dépend de rien) ← `Business` / `Data` ← `Api`.

## Stack technique

| Couche | Technologies |
|---|---|
| **Frontend** | React 19, Vite, TypeScript strict, TailwindCSS, Three.js |
| **Backend** | .NET 9, Clean Architecture, EF Core |
| **LLM** | NVIDIA NIM (compatible OpenAI) → Ollama local, cascade explicite |
| **Embeddings** | `mistral-embed` (1024 dims), fournisseur compatible OpenAI |
| **Voix** | STT Voxtral → Whisper local · TTS Kokoro ONNX · VAD Silero v5 |
| **Base** | PostgreSQL + pgvector (Supabase) |
| **Daemon** | .NET 9 Worker Service, WebSocket, Kokoro TTS |
| **Recherche web** | DuckDuckGo (sans clé), Brave, SerpAPI |

## Structure

```
Orion/
├── backend/
│   ├── Orion.Api/            Controllers · Auth · WebSockets · Middleware · DI
│   ├── Orion.Business/       Agents · LLM · Tools · Daemon · Services
│   ├── Orion.Core/           Entities · DTOs · Interfaces · Configuration
│   ├── Orion.Data/           Repositories · UnitOfWork · OrionDbContext
│   └── Orion.Tests/          xUnit + Moq
├── frontend/src/             components · hooks · services · types (strict)
├── daemon/
│   ├── Orion.Daemon/         Worker · Watchers · Notifiers · Orchestrateur
│   ├── Orion.Daemon.Core/    Interfaces · Entities · Configuration
│   └── Orion.Daemon.Actions/ 20 actions système
├── memory/                   schema.sql · seed.sql (pgvector)
├── tools/definitions/        contrats JSON des outils
└── docs/                     documentation détaillée
```

## Documentation

| Document | Contenu |
|---|---|
| [docs/architecture.md](docs/architecture.md) | 4 couches, LLM en cascade, embeddings, modèle d'authentification |
| [docs/security.md](docs/security.md) | **audit de sécurité**, modèle de menace, injection de prompt |
| [docs/tools.md](docs/tools.md) | catalogue des 24 outils + procédure de création |
| [docs/daemon.md](docs/daemon.md) | Worker Service, watchers, notifiers, installation |
| [docs/voice.md](docs/voice.md) | pipeline voix full-duplex, anti-écho, latence |
| [docs/frontend.md](docs/frontend.md) | PWA, surface unique, flux texte et voix |
| [docs/deployment.md](docs/deployment.md) | déploiement, développement local, variables d'environnement |
| [docs/roadmap.md](docs/roadmap.md) | phases et état d'avancement |
| [AGENTS.md](AGENTS.md) | règles de contribution, contrats immuables, ADRs |

## Prérequis

- **.NET 9 SDK**
- **Node.js 20+**
- **PostgreSQL 15+ avec pgvector** (Supabase free tier suffit)
- **Ollama** *(optionnel)* — uniquement pour le repli LLM hors-ligne
- Une clé API pour un fournisseur compatible OpenAI (embeddings + transcription)

## Démarrage rapide

### 1. Base de données

```bash
psql "$CONNECTION_STRING" -f memory/schema.sql
psql "$CONNECTION_STRING" -f memory/seed.sql
```

### 2. Backend

```bash
cp .env.example .env          # à la racine, puis renseigner les valeurs
dotnet run --project backend/Orion.Api
```

⚠️ `appsettings.json` et `appsettings.Development.json` sont **gitignorés** : aucune configuration
réelle ne vit dans le dépôt. Voir [docs/deployment.md](docs/deployment.md) pour la liste complète
des variables et leur origine en production.

Trois valeurs sont **fail-closed** — absentes, le service refuse de démarrer ou refuse l'accès :

| Variable | Effet si absente |
|---|---|
| `Auth__Password` / `Auth__JwtSecret` | connexion refusée (503) |
| `DAEMON_WS_TOKEN` | WebSocket daemon refusé |
| `AllowedOrigins__0` | **refus de démarrer** (liste vide = toute origine acceptée) |

### 3. Frontend

```bash
cd frontend
cp .env.example .env
npm install
npm run dev            # http://localhost:5173
npm run build          # doit passer : zéro variable/import non utilisé
```

### 4. Daemon (Windows)

```powershell
powershell -File scripts/install-daemon.ps1
```

À rejouer à chaque changement du code du daemon. Le script publie, transporte les voix Kokoro,
conserve la configuration et relance — voir [docs/daemon.md](docs/daemon.md).

## API

Toutes les routes exigent une session (`Authorization: Bearer …`), **sauf** `/api/auth/login`,
`/health` et la coquille de la PWA. C'est un défaut, pas une énumération : une route sans attribut
est refusée.

| Méthode | Route | Description |
|---|---|---|
| POST | `/api/auth/login` | mot de passe → jeton de session |
| POST | `/api/auth/stream-ticket` | billet de 60 s pour SSE / WebSocket |
| POST | `/api/chat` · `/api/chat/stream` | message · streaming SSE |
| GET | `/api/chat/{sessionId}` · `/api/chat/history` | conversation · historique |
| GET/POST/DELETE | `/api/memory` · `/search` · `/{id}` · `/revectorize` | mémoire long terme |
| GET/POST | `/api/deferred-actions` · `/{id}/confirm` · `/{id}/cancel` | file d'actions différées |
| GET/POST | `/api/daemon/status` · `/action` · `/tools` | état et actions machine |
| GET/POST | `/api/tools` · `/api/tools/{name}` | catalogue et exécution d'un outil (gestes du HUD) |
| POST/GET | `/api/voice/transcribe` · `/synthesize` · `/converse` · `/status` | voix |
| GET/POST | `/api/proactivenotification/stream` · `/notify` · `/weights` | proactivité (SSE) |
| GET | `/api/briefing/today` · `/history` | briefing |
| GET | `/health` | sonde conteneur / façade |

WebSockets : `/ws/voice` (billet de flux) · `/daemon` (`X-Daemon-Token`).

## Sécurité

Le modèle d'authentification est **fermé par défaut** et **fail-closed** : une route oubliée est
refusée, un secret absent refuse au lieu d'ouvrir.

Mais l'authentification ne couvre que la moitié du problème. ORION est authentifié comme son
propriétaire **et** agit sur sa machine : une fois la session ouverte, ce que le modèle décide part
avec les droits complets de l'utilisateur. Comme ORION lit le web, une page peut contenir des
instructions qui le détournent — et la requête résultante est parfaitement authentifiée.

C'est pourquoi le garde-fou vit dans le **code** (`IToolInvoker` + `ITool.IsDestructive`), après la
décision du modèle, et non dans une phrase du prompt système.

**[docs/security.md](docs/security.md) est la référence sur ce sujet** : modèle de menace, audit
daté, constats ouverts et leur sévérité. Ce dépôt documente ses failles connues plutôt que de les
taire — les lire avant d'exposer une instance.

## Tests

```bash
cd backend && dotnet test          # 154 méthodes de test sur 21 fichiers
cd daemon  && dotnet test
cd frontend && npm run build       # tsc strict
```

## Écosystème

ORION est conçu comme un assistant autonome, destiné à devenir le moteur IA de l'écosystème
**HexaNexus** une fois mature.

## Licence

Projet personnel — écosystème HexaNexus. Tous droits réservés.
