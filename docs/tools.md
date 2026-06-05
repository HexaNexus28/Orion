# Tools — ORION

## Fonctionnement

```
User → LLM → tool_call({...}) → Backend exécute (service role key) → résultat → LLM → réponse
```

Tout tool implémente `ITool` (`Orion.Core/Interfaces/Tools`) et est enregistré dans `ToolRegistry`.
Jamais de logique inline dans les agents.

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
5. Documenter dans `tools/README.md`
