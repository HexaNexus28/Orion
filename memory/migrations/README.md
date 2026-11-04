# Migrations

Migrations SQL de la base PostgreSQL, à appliquer dans l'ordre après `memory/schema.sql`.

Convention de nommage : `NNN_description.sql`.

| Fichier | Description |
|---|---|
| `001_add_behavior_patterns.sql` | table `behavior_patterns` |
| `002_add_internet_tools_config.sql` | notes sur les outils Internet |
| `003_align_embedding_dimension.sql` | dimension de `memory_vectors.embedding` |
| `004_deferred_actions.sql` | table `deferred_actions` |
| `005_embedding_1024_mistral.sql` | embeddings `mistral-embed`, 1024 dimensions |

## Règles

- Ajouter les index après la création de la table.
- Activer RLS sur les nouvelles tables.
- Les clés d'API restent dans la configuration, jamais en base.
