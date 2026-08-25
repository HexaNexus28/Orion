# Tools — ORION

## Fonctionnement

```
User → LLM → tool_call({...}) → Backend exécute (service role key) → résultat → LLM → réponse
```

Tout tool implémente `ITool` (`Orion.Core/Interfaces/Tools`) et est enregistré dans `ToolRegistry`.
Jamais de logique inline dans les agents.

**L'exécution passe TOUJOURS par `IToolInvoker`** — jamais `tool.ExecuteAsync` en direct. C'est lui,
et lui seul, qui décide d'exécuter, de différer ou de refuser. Avant lui, la boucle agent et l'API
outils exécutaient chacune de leur côté et le garde « daemon absent » était recopié dans les treize
outils système : treize endroits où l'oublier.

### Les trois drapeaux de `ITool`

| Drapeau | Signification | Effet |
|---|---|---|
| `RequiresDaemon` | passe par le PC de l'utilisateur | retiré du catalogue si le PC est éteint, sauf si différable |
| `IsDestructive` | écrit, supprime ou exécute | demande explicite exigée ; au réveil du PC, reconfirmation avant exécution |
| `IsDeferrable` | garde un sens exécuté **plus tard** | mis en file au lieu d'échouer quand le PC est éteint |

`IsDeferrable` se juge sur l'**utilité différée**, pas sur la disponibilité : « ouvre VS Code » ou
« commit le travail » attendent très bien le matin ; « qu'y a-t-il dans ce dossier ? » ne vaut plus
rien demain. Défaut = `false` — un nouvel outil daemon fait rougir `ToolDeferrabilityTests` tant que
son cas n'a pas été tranché, l'oubli ne doit pas valoir « différable ».

⚠️ **`run_script` n'est PAS différable**, alors qu'il agit. Un script est arbitraire : impossible de
savoir s'il lit ou s'il écrit. Quand il l'était, le modèle s'en servait pour contourner le retrait de
`list_files` (`Get-ChildItem`) et promettait pour demain une lecture voulue tout de suite.

## Phase 1 — ShiftStar + Briefing

`get_shiftstar_stats` · `get_shiftstar_votes` · `get_shiftstar_mrr` · `get_shiftstar_tenants` ·
`create_shiftstar_challenge` · `morning_briefing` · `send_notification`

## Phase 2 — Système (via Daemon)

`open_app` · `open_file_in_editor` · `run_script` · `launch_claude` · `open_browser_url` ·
`get_system_status` · `read_file` · `write_file` · `git_status` · `git_commit`

## Phase 3 — Externe + Internet

`get_emails` · `send_email` · `get_calendar` · `web_search` · `web_fetch` · `web_browse` (Playwright) ·
`screenshot_page` · `check_render_deploy` · `check_vercel_deploy` · `get_supabase_logs` ·
`send_whatsapp` · `linkedin_draft`

## Mémoire — ORION se gère lui-même

`memory_save` · `memory_update` · `memory_forget` · `memory_reflect` (dimanche 23h) · `profile_update`

## Créer un nouveau tool

1. Définir le contrat JSON dans `tools/definitions/{tool_name}.json`
2. Implémenter `ITool` dans `Orion.Business/Tools/{ToolName}Tool.cs`
3. Enregistrer dans `ToolRegistry.cs`
4. Si action système → implémenter aussi dans `daemon/actions/` (cf. [daemon.md](daemon.md))
5. **Trancher `IsDeferrable`** : cet outil garde-t-il un sens exécuté au réveil du PC ? Le test
   `ToolDeferrabilityTests` échoue tant que la réponse n'est pas inscrite dans sa liste.
6. Documenter dans `tools/README.md`
