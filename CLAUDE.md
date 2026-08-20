# ORION — CLAUDE.md (index)

ORION = assistant IA personnel de l'utilisateur, futur moteur IA HexaNexus.
Stack : React 19 + Vite (PWA) | .NET 9 backend | Ollama + Claude fallback | Supabase + pgvector | Daemon Windows.
Langue : Français. `AGENTS.md` = règles/agents/phases/ADRs détaillés.

```
orion/
├── backend/    .NET 9 Clean Architecture (Api → Business → Core ← Data)
├── frontend/   React 19 + Vite + TailwindCSS + Three.js (PWA)
├── daemon/     .NET 9 Worker Service Windows (3 projets)
├── memory/     schema.sql, seed.sql
├── tools/      JSON Schema definitions
├── docs/       documentation détaillée (voir index ci-dessous)
└── AGENTS.md   Règles, agents, phases, ADRs
```

## Documentation (docs/)

- [docs/architecture.md](docs/architecture.md) — 4 couches IMMUABLE, structure backend, contrats, LLM, mémoire/RAG, prod vs dev
- [docs/voice.md](docs/voice.md) — pipeline voix full-duplex `/ws/voice`, anti-écho, latence, flow App.tsx
- [docs/frontend.md](docs/frontend.md) — PWA surface unique, structure, flows texte & voix, règles
- [docs/tools.md](docs/tools.md) — catalogue tools (Phase 1-3 + mémoire) + procédure création
- [docs/daemon.md](docs/daemon.md) — Worker Service Windows, watchers, notifiers, install
- [docs/deployment.md](docs/deployment.md) — Render/Vercel/daemon + dev local + .env
- [docs/roadmap.md](docs/roadmap.md) — phases et état

## Invariants NON-NÉGOCIABLES (toujours actifs)

**4 couches** — `Core` (rien) ← `Business`/`Data` ← `Api`. Retours : Data `T?`/`IEnumerable<T>` ·
Business `ApiResponse<T>` · Controller `IActionResult`. Détail : [docs/architecture.md](docs/architecture.md).

**LLM** : tout passe par `IAgentLoop` (jamais d'appel LLM direct depuis un agent ou un service).
Transport via `ILLMAgentClient` — `ILLMClient`/`ILLMRouter` sont l'ancien chemin, sans outils.

⚠️ **`ollama list` NE PROUVE RIEN** : il affiche les `:cloud` en cache local même retirés ou
verrouillés par abonnement. Vérifié le 2026-08-20 : 7 modèles listés, **7 inutilisables**.
Un modèle se vérifie en **l'appelant** — c'est le rôle de `ProbeAsync`, exécutée au démarrage.

`NumCtx` est **obligatoire** dans la config : sans elle Ollama dimensionne le cache KV sur le
contexte maximum du modèle (128k) et réclame ~15 Go pour un modèle de 2 Go → HTTP 500 intermittent.
Config : `backend/Orion.Api/appsettings.json` section `Ollama` + `Agent`.

**Règles dev** :
- Repository Pattern obligatoire couche Data — zéro accès DB ailleurs
- TypeScript strict — no `any`/`as unknown` — types `src/types/`, props `src/props/`
- Frontend : axios via `api.ts` + `endpoints.ts` — jamais `fetch` direct (sauf SSE/stream)
- `npm run build` (tsc) doit passer : zéro variable/import non utilisé
- DTOs dans `Orion.Core`, jamais inline dans controllers
- `ITool` + `ToolRegistry` pour tout tool · action daemon dans la whitelist avant impl
- `CancellationToken` sur toute async DB/réseau, propagé · jamais `.Result`/`.Wait()`
- Toute conversation persistée (aucune exception) · toute nouvelle route = MAJ `endpoints.ts`
- Logs : tool call, daemon action, LLM fallback — tout loggué
- Commits conventionnels (`feat:`/`fix:`/`refactor:`/`chore:`/`docs:`)

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
