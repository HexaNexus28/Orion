# AGENTS.md

Règles à suivre pour toute contribution à ORION, humaine ou assistée par un agent.

## Langues

- Code et identifiants : anglais.
- Documentation et réponses d'ORION : français.

## Règles

### LLM

- Toute inférence passe par `IAgentLoop`. Le transport passe par `ILLMAgentClient`.
- L'ordre du tableau transmis à `LLMCascade` définit la politique de repli.

### Outils

- Un outil implémente `ITool` et s'enregistre deux fois dans `Program.cs` : le type concret, puis
  `ITool`. Sans la seconde ligne, `ToolRegistry` ne le découvre pas.
- L'exécution passe par `IToolInvoker`, seul point de décision entre exécuter, différer et refuser.
- Chaque outil décide `RequiresDaemon`, `IsDestructive` et `IsDeferrable`, et définit son périmètre.

### Daemon

- Une action figure dans `DaemonActionValidator` avant son implémentation.
- La liste autorisée filtre les noms d'actions, pas leurs arguments. Chaque action valide ses
  propres entrées.
- `IAction.ExecuteAsync` retourne `DaemonResponse`, jamais `ApiResponse<T>`.
- Le contrat backend-daemon est du JSON et du binaire sur WebSocket, sans bibliothèque partagée.
- Le daemon communique uniquement avec le backend.

### Sécurité

- Une route sans attribut est refusée (`FallbackPolicy`). Les exceptions sont explicites.
- Ne pas poser `[AllowAnonymous]` au niveau d'un contrôleur : il l'emporte sur tout `[Authorize]`.
- Un secret absent refuse l'accès.
- Une protection contre les actions du modèle vit dans le code, après sa décision. Une consigne de
  prompt n'est pas une protection.
- Disque : `PathScope`, résolu avant tout accès, en ouvrant le chemin qu'il retourne.
- Réseau : `UrlScope`, vérifié avant la requête, avec une revalidation à chaque redirection.
- Un périmètre vide n'autorise rien.
- Une option de sécurité déclarée doit être lue par le code qui agit.
- Aucun secret, aucune donnée personnelle et aucun hôte réel dans le dépôt.

### Données

- Repository Pattern obligatoire : aucun accès à la base hors de `Orion.Data`.
- Toute conversation est persistée.
- Modèle et dimension d'embedding sont stockés avec chaque vecteur. Changer de modèle impose une
  revectorisation complète.
- Toute table liée à un utilisateur active RLS.

### Frontend

- TypeScript strict, sans `any` ni `as unknown`.
- Types partagés dans `src/types/`.
- Appels via `api.ts` et `endpoints.ts`, sauf flux SSE.
- Notifications proactives par SSE, sans polling.
- Conversation vocale : un seul chemin sonore, le WAV reçu sur `/ws/voice` et joué par
  `AudioContext`. L'API Web Speech n'est pas utilisée, car l'annulation d'écho du navigateur ne
  couvre pas sa sortie.
- `HologramResponsePanel` reste en Three.js pur, sans HTML.

### Code

- `CancellationToken` propagé sur tout appel asynchrone de base de données ou de réseau.
- Jamais `.Result` ni `.Wait()`.
- DTOs dans `Orion.Core`.
- Retours : Data `T?` ou `IEnumerable<T>`, Business `ApiResponse<T>`, contrôleur `IActionResult`.
- Aucun code mort.
- Les commentaires expliquent une contrainte technique. Ils ne racontent pas l'historique.

### Documentation et commits

- La documentation décrit l'état actuel, au présent et sur un ton neutre.
- Pas d'historique des corrections, de date d'incident ni de mention de qui a demandé quoi.
- Toute modification de comportement met à jour la documentation dans le même commit.
- Commits conventionnels : un sujet qui décrit le changement, un corps qui donne la raison technique.

## Agents du backend

| Classe | Rôle |
|---|---|
| `AgentLoop` | boucle multi-tours : prompt, streaming, appels d'outils, événements typés |
| `ConversationAgent` | prépare le contexte, persiste et diffuse la réponse |
| `BriefingAgent` | génère le briefing, déclenché par `BriefingScheduler` |

La mémoire est portée par des services : `MemoryService`, `MemoryConsolidator`, `MemoryRevectorizer`.

## Décisions d'architecture

| Sujet | Décision | Raison |
|---|---|---|
| Frontend | React et Vite | PWA sans besoin de rendu serveur |
| Daemon | Worker .NET | même stack que le backend, extensible via `IAction` |
| Connexion du daemon | sortante vers le backend | fonctionne derrière un pare-feu et une IP dynamique |
| Contrat daemon | JSON sur WebSocket | backend et daemon se déploient séparément |
| Mémoire | pgvector | pas d'infrastructure vectorielle séparée |
| Streaming texte | SSE | flux unidirectionnel, plus simple qu'un WebSocket |
| Retours | `ApiResponse<T>` en Business | la couche métier connaît le sens de l'erreur |
| Interface 3D | `@react-three/fiber` | Three.js sous forme de composants React |
| Gestes | MediaPipe | exécution dans le navigateur, sans serveur |
| LLM | cascade explicite NIM, puis Ollama | ordre lisible, fonctionnement dégradé hors ligne |
| Modèle servi | élu au démarrage par `ProbeAsync` | seul un appel réel prouve la disponibilité d'un modèle |
| Embeddings | `mistral-embed`, 1024 dimensions | disponible en production et indexable par pgvector |
| Exécution d'outil | `IToolInvoker` unique | une seule politique, pas de contrôle à recopier |
| Actions destructives | confirmation imposée par le code | résiste à une injection de prompt |
| Authentification | fermée par défaut | une route oubliée reste protégée |
| Périmètre des outils | appliqué dans le code qui agit | une configuration non lue ne protège pas |
| Transcription et synthèse | cascades, service distant puis local | latence faible, repli en cas de panne |
| Client vocal | une seule connexion active | évite que deux micros captent la même voix |
