# ORION

Assistant IA personnel agentique. Stack : React 19 + Vite (PWA), .NET 9, PostgreSQL + pgvector,
daemon Windows.

Le dépôt est public : aucun secret, aucune donnée personnelle, aucun nom d'hôte réel dans un
fichier versionné.

## Documentation

- [AGENTS.md](AGENTS.md) : règles de contribution et décisions d'architecture
- [docs/architecture.md](docs/architecture.md) : couches, cascades, mémoire, authentification
- [docs/data-model.md](docs/data-model.md) : tables et migrations
- [docs/tools.md](docs/tools.md) : catalogue d'outils et création d'un outil
- [docs/voice.md](docs/voice.md) : pipeline vocal
- [docs/proactivity.md](docs/proactivity.md) : watchers et décision proactive
- [docs/daemon.md](docs/daemon.md) : daemon Windows
- [docs/frontend.md](docs/frontend.md) : PWA
- [docs/deployment.md](docs/deployment.md) : configuration, déploiement, CI
- [docs/security.md](docs/security.md) : modèle de menace et contrôles
- [docs/roadmap.md](docs/roadmap.md) : état et prochaines étapes

## Invariants

- Dépendances : `Core` ← `Business` / `Data` ← `Api`.
- Retours : Data `T?` ou `IEnumerable<T>`, Business `ApiResponse<T>`, contrôleur `IActionResult`.
- Tout appel LLM passe par `IAgentLoop`, le transport par `ILLMAgentClient`.
- Toute exécution d'outil passe par `IToolInvoker`, jamais par `tool.ExecuteAsync`.
- Le refus est le comportement par défaut : `FallbackPolicy` exige le propriétaire et un secret
  absent refuse l'accès.
- Un outil qui touche au disque, au réseau interne ou aux processus applique un périmètre explicite
  dans le code qui agit.
- La confirmation d'une action destructive est décidée par le code, après la décision du modèle.

## Règles de développement

- Repository Pattern pour tout accès aux données.
- TypeScript strict, sans `any` ni `as unknown`.
- Appels HTTP du frontend via `api.ts` et `endpoints.ts`, sauf flux SSE.
- `npm run build` doit passer.
- DTOs dans `Orion.Core`.
- Nouvel outil : enregistrer le type concret puis `ITool` dans `Program.cs`, et décider
  `IsDeferrable`, `IsDestructive` et le périmètre.
- Action daemon : ajouter le nom à la liste autorisée avant l'implémentation.
- `CancellationToken` propagé sur tout appel asynchrone de base de données ou de réseau.
- Conversations toujours persistées.
- Commits conventionnels : `feat:`, `fix:`, `refactor:`, `chore:`, `docs:`.

## Style de la documentation et des commits

La documentation décrit l'état actuel du système, au présent et sur un ton neutre. Elle ne contient
ni historique des corrections, ni dates d'incident, ni échanges de travail, ni mention de qui a
demandé quoi. L'historique appartient à git. Les messages de commit décrivent le changement et sa
raison technique.

## graphify

Le graphe de connaissances se trouve dans `graphify-out/`.

- Pour une question sur le code, lancer d'abord `graphify query "<question>"` si
  `graphify-out/graph.json` existe. `graphify path "<A>" "<B>"` donne une relation et
  `graphify explain "<concept>"` un concept.
- Si `graphify-out/wiki/index.md` existe, l'utiliser pour la navigation générale.
- Lire `graphify-out/GRAPH_REPORT.md` seulement pour une revue d'architecture.
- Après une modification du code, lancer `graphify update .`.
