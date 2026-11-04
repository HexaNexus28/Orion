# ORION

ORION est un assistant IA personnel agentique. Il converse à l'écrit et à la voix, exécute des
actions sur un poste Windows, conserve une mémoire à long terme et peut prendre l'initiative
d'une notification.

Le projet est en développement actif : l'API et les schémas peuvent évoluer.

## Fonctionnalités

| Domaine | Description |
|---|---|
| Agent | boucle multi-tours (`AgentLoop`) qui enchaîne des outils en streaming |
| Actions | un daemon Windows donne accès aux fichiers, à git, aux processus, au presse-papiers et à l'écran |
| Mémoire | recherche vectorielle pgvector, consolidation automatique, profil utilisateur |
| Voix | conversation full-duplex par WebSocket, détection de parole Silero |
| Proactivité | des watchers observent l'activité, un décideur choisit de notifier, de différer ou de se taire |
| Résilience | cascades de fournisseurs pour le LLM, la transcription et la synthèse vocale |
| File différée | les actions demandées quand le poste est éteint s'exécutent à son retour |

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  Frontend : React 19, Vite, PWA                              │
│    Scène 3D · HUD · VAD Silero · SSE · WebSocket /ws/voice   │
├──────────────────────────────────────────────────────────────┤
│  Backend : .NET 9, Clean Architecture                        │
│    IAgentLoop → ILLMAgentClient (LLMCascade)                 │
│    IToolInvoker : exécute, diffère ou refuse                 │
│    Mémoire : pgvector + mistral-embed                        │
│    Authentification : JWT et secret partagé du daemon        │
├──────────────────────────────────────────────────────────────┤
│  Daemon : .NET 9 Worker, session utilisateur Windows         │
│    Connexion sortante vers le backend · 20 actions           │
│    Watchers · notifications · synthèse Kokoro locale         │
├──────────────────────────────────────────────────────────────┤
│  Données : PostgreSQL + pgvector (Supabase)                  │
└──────────────────────────────────────────────────────────────┘
```

## Stack

| Couche | Technologies |
|---|---|
| Frontend | React 19, Vite, TypeScript strict, TailwindCSS, Three.js |
| Backend | .NET 9, EF Core |
| LLM | NVIDIA NIM (compatible OpenAI), repli Ollama |
| Embeddings | `mistral-embed`, 1024 dimensions |
| Transcription | Voxtral, repli Whisper.net |
| Synthèse vocale | Voxtral TTS, repli Kokoro |
| Base de données | PostgreSQL 15+ avec pgvector |
| Recherche web | DuckDuckGo, Brave, SerpAPI |

## Structure du dépôt

```
backend/
  Orion.Api/        contrôleurs, authentification, WebSockets, composition
  Orion.Business/   agents, LLM, outils, services
  Orion.Core/       entités, DTOs, interfaces, configuration
  Orion.Data/       repositories, unit of work, DbContext
  Orion.Tests/      tests xUnit et Moq
daemon/
  Orion.Daemon/          worker, watchers, notificateurs
  Orion.Daemon.Core/     interfaces, entités, configuration, périmètre disque
  Orion.Daemon.Actions/  actions système
  Orion.Daemon.Tests/    tests du daemon
frontend/src/       composants, hooks, services, types
memory/             schéma SQL, données initiales, migrations
scripts/            installation du daemon
tools/definitions/  contrats JSON d'outils
docs/               documentation
```

## Prérequis

- SDK .NET 9
- Node.js 20 ou supérieur
- PostgreSQL 15 ou supérieur avec l'extension pgvector
- Une clé Mistral pour les embeddings, la transcription et la synthèse vocale
- Une clé NVIDIA NIM, ou Ollama pour un fonctionnement local
- Windows pour le daemon

## Démarrage

### Base de données

```bash
psql "$CONNECTION_STRING" -f memory/schema.sql
psql "$CONNECTION_STRING" -f memory/seed.sql
```

Appliquer ensuite les fichiers de `memory/migrations/` dans l'ordre.

### Backend

```bash
cp .env.example .env
dotnet run --project backend/Orion.Api
```

L'API écoute sur `http://localhost:5107`. Les variables sont décrites dans
[docs/deployment.md](docs/deployment.md).

### Frontend

```bash
cd frontend
cp .env.example .env
npm install
npm run dev
```

L'interface est servie sur `http://localhost:3000`.

### Daemon

```powershell
powershell -File scripts/install-daemon.ps1
```

Voir [docs/daemon.md](docs/daemon.md).

## Tests

```bash
dotnet test backend/Orion.sln
dotnet test daemon/Orion.Daemon.sln
cd frontend && npm run build
```

## Documentation

| Document | Contenu |
|---|---|
| [docs/architecture.md](docs/architecture.md) | couches, cascades, mémoire, authentification |
| [docs/data-model.md](docs/data-model.md) | tables et migrations |
| [docs/tools.md](docs/tools.md) | catalogue d'outils et création d'un outil |
| [docs/voice.md](docs/voice.md) | pipeline vocal |
| [docs/proactivity.md](docs/proactivity.md) | watchers et décision proactive |
| [docs/daemon.md](docs/daemon.md) | daemon Windows |
| [docs/frontend.md](docs/frontend.md) | PWA |
| [docs/deployment.md](docs/deployment.md) | configuration, déploiement, CI |
| [docs/security.md](docs/security.md) | modèle de menace, contrôles, signalement |
| [docs/roadmap.md](docs/roadmap.md) | état et prochaines étapes |
| [AGENTS.md](AGENTS.md) | règles de contribution et décisions d'architecture |

## Licence

Distribué sous licence MIT. Voir [LICENSE](LICENSE).
