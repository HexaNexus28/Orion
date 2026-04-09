# ORION Database Migrations

Ce dossier contient les migrations SQL pour la base de données Supabase PostgreSQL.

## Convention de nommage

```
XXX_description_courte.sql
```

Exemples:
- `001_add_behavior_patterns.sql`
- `002_add_internet_tools_config.sql`
- `003_add_user_preferences.sql`

## Comment appliquer une migration

### Option 1: Supabase SQL Editor (développement)

1. Ouvrir https://app.supabase.com
2. Aller dans "SQL Editor"
3. Créer une "New query"
4. Copier/coller le contenu de la migration
5. Run

### Option 2: Schema complet (reset)

Pour réinstaller from scratch (⚠️ perd toutes les données):

```sql
-- Lire et exécuter schema.sql
\i ../schema.sql
```

## Migrations existantes

| Fichier | Description | Date |
|---------|-------------|------|
| `001_add_behavior_patterns.sql` | Table behavior_patterns pour la détection de patterns utilisateur | 2025-04-07 |
| `002_add_internet_tools_config.sql` | Documentation tools Phase 3 (config reste en appsettings) | 2025-04-07 |

## Notes

- `DROP TABLE IF EXISTS ... CASCADE` permet de réexécuter une migration sans erreur
- Toujours ajouter les index après la création de table
- Toujours activer RLS sur les nouvelles tables
- Les clés API (Brave, SerpAPI, Anthropic) restent dans appsettings.json, jamais en DB
