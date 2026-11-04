# Architecture

## Couches

```
Orion.Core      entités, DTOs, interfaces, configuration ; aucune dépendance
Orion.Business  agents, LLM, outils, services ; dépend de Core
Orion.Data      repositories, unit of work, DbContext ; dépend de Core
Orion.Api       contrôleurs, WebSockets, composition ; dépend de Business et Data

Orion.Daemon.Core     interfaces, entités, configuration ; aucune dépendance
Orion.Daemon.Actions  actions système ; dépend de Daemon.Core
Orion.Daemon          worker, watchers, notificateurs ; dépend des deux
```

| Couche | Type de retour |
|---|---|
| Data | `T?` ou `IEnumerable<T>` |
| Business | `ApiResponse<T>` |
| Contrôleur | `IActionResult` |

Le backend et le daemon échangent du JSON et du binaire sur WebSocket. Chaque côté définit ses
propres types.

## Traitement d'une requête

```
Message utilisateur
  → ConversationAgent.PrepareStreamAsync   persistance, profil, souvenirs, prompt
  → ConversationAgent.StreamLLMAsync
      → AgentLoop                           boucle multi-tours
          → LLMCascade                      génération en streaming
          → IToolInvoker                    exécute, diffère ou refuse
  → réponse persistée, épisode enregistré en mémoire
```

`AgentLoop` émet des événements typés : `Token`, `ToolStart`, `ToolResult`, `Done`, `Error`.

## Cascades de fournisseurs

Chaque cascade essaie ses fournisseurs dans l'ordre déclaré dans `Program.cs`.

| Fonction | Interface | Ordre |
|---|---|---|
| LLM | `ILLMAgentClient` (`LLMCascade`) | `NimAgentClient`, puis `OllamaAgentClient` |
| Transcription | `IWhisperService` (`TranscriptionCascade`) | `VoxtralTranscriptionService`, puis `WhisperService` |
| Synthèse vocale | `ISpeechSynthesizer` (`SpeechCascade`) | `VoxtralSpeechService`, puis `VoiceNotificationService` (Kokoro) |

Le modèle LLM servi est élu au démarrage par `ProbeAsync`, qui appelle réellement le fournisseur.

`Ollama:NumCtx` est obligatoire : sans cette valeur, Ollama dimensionne son cache sur le contexte
maximal du modèle, ce qui provoque des erreurs de mémoire.

## Mémoire

- Court terme : derniers messages de la conversation.
- Long terme : table `memory_vectors`, recherche par similarité cosinus.
- Embeddings : `IEmbeddingService`, `mistral-embed`, 1024 dimensions.

La mémoire est organisée en emplacements (`MemorySlot`) :

| Emplacement | Contenu | Écriture |
|---|---|---|
| `Episode` | échange brut, avant consolidation | ajout |
| `Rules` | règles de comportement | ajout |
| `Decisions` | décisions durables et leur raison | ajout |
| `State` | informations en cours | remplacement |
| `Refs` | références stables | ajout |

Chaque tour enregistre un épisode. `MemoryConsolidationScheduler` déclenche `MemoryConsolidator`,
qui distille les épisodes vers les autres emplacements.

La recherche mémoire n'est pas bloquante : si les embeddings sont indisponibles, la conversation
continue sans souvenirs. La persistance des conversations est obligatoire : si la base est
inaccessible, le tour échoue.

Un embedding dépend de son modèle. Modèle et dimension sont stockés avec chaque vecteur et vérifiés
au démarrage. `MemoryRevectorizer` recalcule la table lors d'un changement de modèle.

## Services d'arrière-plan

| Service | Rôle |
|---|---|
| `BriefingScheduler` | génère le briefing quotidien |
| `MemoryConsolidationScheduler` | consolide la mémoire |
| `DeferredActionWatcher` | traite la file au retour du daemon et expire les actions périmées |
| `HudBroadcastService` | alimente les widgets du HUD |

## Topologie

```
Navigateur ── HTTPS, SSE ───────────► Backend
Navigateur ── WSS /ws/voice ────────► Backend
Daemon     ── WSS /daemon ──────────► Backend
Backend    ── HTTPS ────────────────► Fournisseurs IA
Backend    ── PostgreSQL ───────────► Supabase
```

En production, le backend sert aussi le bundle de la PWA depuis `wwwroot`. Le daemon initie sa
connexion, ce qui évite d'ouvrir un port sur le poste.

## Authentification

| Appelant | Preuve |
|---|---|
| Propriétaire | JWT obtenu par mot de passe |
| Daemon | secret partagé, en-tête `X-Daemon-Token` |

- `FallbackPolicy` exige le rôle propriétaire. Les exceptions sont `/api/auth/login`, `/health` et
  la coquille de la PWA.
- Un secret absent refuse l'accès.
- SSE et WebSocket de navigateur ne peuvent pas porter d'en-tête. Ils utilisent un billet de 60 s à
  audience distincte, accepté uniquement sur les chemins de flux.
- Les origines WebSocket reprennent la liste CORS. Une liste vide empêche le démarrage.

Voir [security.md](security.md).

## Configuration

`appsettings.json` n'est pas versionné. En production, les valeurs arrivent par variables
d'environnement. Voir [deployment.md](deployment.md).
