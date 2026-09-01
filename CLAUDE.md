# ORION — CLAUDE.md (index)

ORION = assistant IA personnel **agentique**, futur moteur IA HexaNexus.
Stack : React 19 + Vite (PWA) | .NET 9 backend | **cascade NVIDIA NIM → Ollama local** |
Supabase + pgvector | Daemon Windows.
`AGENTS.md` = règles/agents/phases/ADRs détaillés.

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

- [docs/architecture.md](docs/architecture.md) — 4 couches IMMUABLE, structure backend, contrats, LLM en cascade, embeddings, prod vs dev, modèle d'authentification
- [docs/security.md](docs/security.md) — **audit sécurité**, modèle de menace, injection de prompt, constats ouverts
- [docs/voice.md](docs/voice.md) — pipeline voix full-duplex `/ws/voice`, anti-écho, latence, flow App.tsx
- [docs/frontend.md](docs/frontend.md) — PWA surface unique, structure, flows texte & voix, règles
- [docs/tools.md](docs/tools.md) — catalogue réel des 24 outils + procédure création
- [docs/daemon.md](docs/daemon.md) — Worker Service Windows, watchers, notifiers, install
- [docs/deployment.md](docs/deployment.md) — VPS + Nginx + daemon, dev local, variables d'environnement
- [docs/roadmap.md](docs/roadmap.md) — phases et état

## Invariants non négociables

**4 couches** — `Core` (rien) ← `Business`/`Data` ← `Api`. Retours : Data `T?`/`IEnumerable<T>` ·
Business `ApiResponse<T>` · Controller `IActionResult`. Détail : [docs/architecture.md](docs/architecture.md).

**LLM** : tout passe par `IAgentLoop` (jamais d'appel LLM direct depuis un agent ou un service).
Transport via `ILLMAgentClient` — `ILLMClient`/`ILLMRouter` sont l'ancien chemin, sans outils.

**`ollama list` ne prouve rien** : il affiche les `:cloud` en cache local même retirés ou
verrouillés par abonnement. Vérifié le 2026-08-20 : 7 modèles listés, **7 inutilisables**.
Un modèle se vérifie en **l'appelant** — c'est le rôle de `ProbeAsync`, exécutée au démarrage.

`NumCtx` est **obligatoire** dans la config : sans elle Ollama dimensionne le cache KV sur le
contexte maximum du modèle (128k) et réclame ~15 Go pour un modèle de 2 Go → HTTP 500 intermittent.
Config : `backend/Orion.Api/appsettings.json` sections `Ollama` + `Agent` — ce fichier est
**gitignoré, donc absent du dépôt**. En production les valeurs arrivent par variables
d'environnement (`Ollama__NumCtx`, …). Modèle complet : `.env.example`.

**Sécurité — fermé par défaut, et le défaut est le REFUS.** `FallbackPolicy` exige le propriétaire :
une route sans attribut est refusée, jamais ouverte. Un secret absent REFUSE au lieu d'ouvrir
(`DAEMON_WS_TOKEN`, `Auth:JwtSecret`).

**L'authentification ne protège que la moitié du problème** : ORION est authentifié comme son
propriétaire ET agit sur sa machine. Il lit le web, donc une page peut détourner le modèle — et la
requête résultante est parfaitement authentifiée. Le garde-fou doit vivre dans le CODE, après la
décision du modèle (`IToolInvoker` + `IsDestructive`), jamais dans une phrase du prompt.

**Un outil qui touche au disque, au réseau interne ou aux processus doit porter un PÉRIMÈTRE
explicite, appliqué dans le code qui agit.** Une option déclarée et jamais lue est pire que son
absence : elle se fait passer pour une défense. Constats ouverts : [docs/security.md](docs/security.md).

**Règles dev** :
- Repository Pattern obligatoire couche Data — zéro accès DB ailleurs
- TypeScript strict — no `any`/`as unknown` — types `src/types/`, props `src/props/`
- Frontend : axios via `api.ts` + `endpoints.ts` — jamais `fetch` direct (sauf SSE/stream)
- `npm run build` (tsc) doit passer : zéro variable/import non utilisé
- DTOs dans `Orion.Core`, jamais inline dans controllers
- `ITool` + `ToolRegistry` pour tout tool · action daemon dans la whitelist avant impl
- **Exécution d'outil : toujours via `IToolInvoker`**, jamais `tool.ExecuteAsync` en direct —
  c'est le point unique qui décide d'exécuter, différer (PC éteint) ou refuser. Tout nouvel
  outil daemon doit trancher `IsDeferrable`, `IsDestructive` **et son périmètre**
  (cf. [docs/tools.md](docs/tools.md))
- Nouvel outil = enregistré **DEUX fois** dans `Program.cs` (`AddScoped<MonTool>()` puis
  `AddScoped<ITool>(sp => sp.GetRequiredService<MonTool>())`) — sans la seconde, `ToolRegistry`
  ne le découvre pas et le modèle ne le voit jamais
- `CancellationToken` sur toute async DB/réseau, propagé · jamais `.Result`/`.Wait()`
- Toute conversation persistée (aucune exception) · toute nouvelle route = MAJ `endpoints.ts`
- Logs : tool call, daemon action, LLM fallback — tout loggué
- Commits conventionnels (`feat:`/`fix:`/`refactor:`/`chore:`/`docs:`)
