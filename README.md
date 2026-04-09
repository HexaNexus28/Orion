# ORION — Assistant IA Personnel

> Assistant IA personnel de Yawo Zoglo — futur moteur IA d'HexaNexus.

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                        ORION STACK                           │
│                                                              │
│  Frontend (React 19 + Vite)                                  │
│    ├── Entité 3D interactive (Three.js)                      │
│    ├── Voice Activity Detection (VAD)                        │
│    ├── SSE streaming token par token                         │
│    └── TTS playback via AudioContext                         │
│                                                              │
│  Backend (.NET 9 API)                                        │
│    ├── ConversationAgent → MemoryAgent → ToolAgent           │
│    ├── LLMRouter (Ollama local, fallback Claude)             │
│    ├── RAG : pgvector + nomic-embed-text                     │
│    ├── Tools : web_search, web_fetch, ShiftStar stats...     │
│    └── SSE streaming + audit complet                         │
│                                                              │
│  Daemon (Windows service .NET 9)                             │
│    ├── WebSocket ↔ Backend                                   │
│    ├── Actions : open_app, run_script, git, TTS (Kokoro)     │
│    ├── Watchers proactifs (système, temps, activité)         │
│    └── Notifiers (Toast, TTS SAPI, Kokoro ONNX)             │
│                                                              │
│  Data (Supabase)                                             │
│    ├── conversations, messages, memory_vectors               │
│    ├── user_profile (clé-valeur)                             │
│    └── audit_logs, tool_executions                           │
└──────────────────────────────────────────────────────────────┘
```

## Stack Technique

| Couche | Technologies |
|--------|-------------|
| **Frontend** | React 19, Vite, TypeScript, TailwindCSS, Three.js |
| **Backend** | .NET 9, Clean Architecture (Api → Business → Core → Data) |
| **LLM** | Ollama (Kimi K2 / Qwen), fallback Claude API |
| **Embeddings** | nomic-embed-text (768 dims) via Ollama |
| **Database** | Supabase (PostgreSQL + pgvector) |
| **Daemon** | .NET 9 Worker Service, WebSocket, Kokoro TTS |
| **Recherche** | DuckDuckGo (gratuit), Brave, SerpAPI |

## Structure du Projet

```
Orion/
├── backend/
│   ├── Orion.Api/          # Controllers, middleware, DI
│   ├── Orion.Business/     # Agents, LLM, Tools, Services
│   ├── Orion.Core/         # Entities, DTOs, Interfaces
│   ├── Orion.Data/         # Repositories, Supabase context
│   └── Orion.Tests/        # xUnit + Moq (33 tests)
├── frontend/               # React 19 + Vite
│   └── src/
│       ├── components/     # Entity 3D, hologram, UI
│       ├── hooks/          # useVAD, useStream, useChat, useVoice
│       ├── services/       # API clients (axios + SSE)
│       └── types/          # TypeScript strict (no any)
├── daemon/
│   ├── Orion.Daemon/       # Worker, WebSocket, Watchers, Notifiers
│   ├── Orion.Daemon.Core/  # Interfaces, Entities, Config
│   └── Orion.Daemon.Actions/ # open_app, git, run_script, TTS...
├── memory/                 # SQL migrations (pgvector)
├── docs/                   # Architecture docs
└── tools/                  # Tool definitions JSON
```

## Prérequis

- **.NET 9 SDK**
- **Node.js 20+**
- **Ollama** avec les modèles :
  ```bash
  ollama pull kimi-k2
  ollama pull nomic-embed-text
  ```
- **Supabase** projet (free tier suffit)

## Démarrage Rapide

### 1. Backend
```bash
cd backend
cp Orion.Api/appsettings.Development.json.example Orion.Api/appsettings.Development.json
# Configurer Supabase URL + Key dans appsettings
dotnet run --project Orion.Api
```

### 2. Frontend
```bash
cd frontend
cp .env.example .env
npm install
npm run dev
```

### 3. Daemon (Windows)
```bash
cd daemon/Orion.Daemon
cp appsettings.Development.json.example appsettings.Development.json
dotnet run
```

## API Endpoints

| Méthode | Route | Description |
|---------|-------|-------------|
| POST | `/api/chat` | Envoyer un message |
| POST | `/api/chat/stream` | SSE streaming token par token |
| GET | `/api/chat/{id}` | Récupérer une conversation |
| GET | `/api/chat/history` | Historique paginé |
| GET | `/api/memory` | Lister les souvenirs |
| POST | `/api/voice/transcribe` | Whisper STT |
| POST | `/api/voice/synthesize` | Kokoro TTS |
| GET | `/api/health` | Health check |
| GET | `/api/briefing/today` | Briefing du jour |

## Recherche Web

ORION supporte 3 providers de recherche (configurable dans `appsettings.json`) :

| Provider | Clé API | Config |
|----------|---------|--------|
| **DuckDuckGo** | Aucune (gratuit) | `"SearchApiProvider": "duckduckgo"` |
| Brave | Requise | `"BraveApiKey": "..."` |
| SerpAPI | Requise | `"SerpApiKey": "..."` |

Par défaut : **DuckDuckGo** (aucune clé requise).

## Mémoire

Deux niveaux :
- **Court terme** : 20 derniers messages de la session
- **Long terme** : Supabase + pgvector (RAG sémantique, profil utilisateur)

Chaque message est vectorisé via `nomic-embed-text` (768 dims) et stocké pour recherche par similarité cosinus.

## Tests

```bash
cd backend
dotnet test   # 33 tests (ChatService, AuditService, Repositories, Controllers)
```

## Écosystème HexaNexus

```
ORION (IA standalone)
  └── une fois mature → moteur IA de
HEXANEXUS (produit multi-tenant PME/TPE)
  └── alimenté par ShiftStar, ORION
```

## Licence

Projet privé — Yawo Zoglo / HexaNexus.