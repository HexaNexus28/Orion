# ORION

Les règles du projet sont dans [AGENTS.md](AGENTS.md) et la documentation dans [docs/](docs/).

Le dépôt est public : aucun secret, aucune donnée personnelle, aucun nom d'hôte réel dans un
fichier versionné.

## graphify

Le graphe de connaissances se trouve dans `graphify-out/`.

- Pour une question sur le code, lancer d'abord `graphify query "<question>"` si
  `graphify-out/graph.json` existe. `graphify path "<A>" "<B>"` donne une relation et
  `graphify explain "<concept>"` un concept.
- Si `graphify-out/wiki/index.md` existe, l'utiliser pour la navigation générale.
- Lire `graphify-out/GRAPH_REPORT.md` seulement pour une revue d'architecture.
- Après une modification du code, lancer `graphify update .`.
