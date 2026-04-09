# ORION Tools Definitions

Ce dossier contient les définitions JSON Schema de tous les tools disponibles pour ORION.

## Format

Chaque fichier `.json` suit la spécification OpenAI Function Calling / Claude Tool Use :

```json
{
  "name": "tool_name",
  "description": "Description claire pour le LLM",
  "parameters": { /* JSON Schema */ },
  "returns": { /* JSON Schema optionnel */ }
}
```

## Tools disponibles

### Phase 3 - Internet Tools

| Tool | Fichier | Description |
|------|---------|-------------|
| `web_search` | `web_search.json` | Recherche web via Brave/SerpAPI |
| `web_fetch` | `web_fetch.json` | Extraction texte d'une URL |
| `web_browse` | `web_browse.json` | Navigation interactive Playwright |
| `screenshot_page` | `screenshot_page.json` | Capture page → base64 |

### Phase 2 - ShiftStar Tools (à créer)

| Tool | Description |
|------|-------------|
| `get_shiftstar_stats` | Stats du jour (votes, notes, MRR) |
| `get_shiftstar_votes` | Derniers votes |
| `get_shiftstar_mrr` | MRR et churn |
| `get_shiftstar_tenants` | Liste tenants actifs |
| `create_challenge` | Créer un défi gamifié |

### Phase 4 - Daemon Tools

| Tool | Description |
|------|-------------|
| `open_app` | Ouvrir une application locale |
| `run_command` | Exécuter une commande shell |
| `get_system_info` | Récupérer infos système |

## Utilisation

Ces définitions sont utilisées par :
- **Backend** : `ToolRegistry.cs` charge dynamiquement les schemas
- **Frontend** : `ToolCard.tsx` affiche les paramètres
- **LLM** : Passé en `tools` array dans l'API Claude/Ollama

## Ajouter un tool

1. Créer `tools/definitions/mon_tool.json`
2. Implémenter `MonTool.cs` dans `Orion.Business/Tools/`
3. Enregistrer dans `Program.cs` : `builder.Services.AddScoped<MonTool>()`
