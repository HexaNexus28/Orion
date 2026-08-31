# Tools — ORION

> État réel du catalogue au 2026-08-27. Les phases historiques listaient des outils
> (`get_shiftstar_stats`, `get_emails`, `send_whatsapp`, `check_render_deploy`…) qui n'ont
> **jamais été implémentés** : ils ont été retirés de cette page. Ce qui suit est ce que
> `ToolRegistry` expose réellement au modèle — 24 outils.

## Fonctionnement

```
User  → AgentLoop → tool_call({...}) ─┐
                                      ├→ IToolInvoker → [exécute | diffère | refuse] → résultat
HUD   → POST /api/tools/{name} ───────┘
```

**Deux entrées, un seul chemin.** Un geste du HUD n'est pas un second mécanisme à sécuriser :
il emprunte le même `IToolInvoker`, donc le même garde-fou. Un bouton « commit » part en file de
confirmation exactement comme si le modèle l'avait demandé. Seule l'`Origin` diffère (`hud` vs
`chat`), pour qu'ORION sache d'où vient une action mise en attente.

Tout outil implémente `ITool` (`Orion.Core/Interfaces/Tools/ITool.cs`) et est découvert par
`ToolRegistry` via l'enregistrement DI `AddScoped<ITool>(…)` dans `Program.cs`. Jamais de logique
inline dans les agents.

**L'exécution passe TOUJOURS par `IToolInvoker`** — jamais `tool.ExecuteAsync` en direct. C'est
lui, et lui seul, qui décide d'exécuter, de différer ou de refuser. Avant lui, la boucle agent et
l'API outils exécutaient chacune de leur côté et le garde « daemon absent » était recopié dans les
treize outils système : treize endroits où l'oublier.

### Les quatre membres de `ITool`

| Membre | Défaut | Signification |
|---|---|---|
| `RequiresDaemon` | `false` | passe par le PC de l'utilisateur ; retiré du catalogue si le PC est éteint, sauf si différable |
| `IsDestructive` | `false` | écrit, supprime ou exécute → **confirmation exigée, PC allumé compris** |
| `IsDeferrable` | `false` | garde un sens exécuté **plus tard** → mis en file au lieu d'échouer |
| `BuildCard(result)` | `null` | carte du HUD produite à partir du résultat |

`IsDeferrable` se juge sur l'**utilité différée**, pas sur la disponibilité : « ouvre VS Code » ou
« commit le travail » attendent très bien le matin ; « qu'y a-t-il dans ce dossier ? » ne vaut plus
rien demain. Défaut = `false` — un nouvel outil daemon fait rougir `ToolDeferrabilityTests` tant que
son cas n'a pas été tranché ; l'oubli ne doit pas valoir « différable ».

⚠️ **`run_script` n'est PAS différable**, alors qu'il agit. Un script est arbitraire : impossible de
savoir s'il lit ou s'il écrit. Quand il l'était, le modèle s'en servait pour contourner le retrait
de `list_files` (`Get-ChildItem`) et promettait pour demain une lecture voulue tout de suite.

### `IsDestructive` est un garde-fou de CODE, pas de prompt

Depuis `b011dbe`, `ToolInvoker` met en file **toute** action destructive, même PC allumé. Le
garde-fou vivait auparavant dans une phrase du prompt système — c'est-à-dire une suggestion. Un
modèle qui décide d'agir agit. Le contrôle est désormais placé **après** la décision du modèle,
seul endroit où il puisse tenir face à une injection de prompt (cf. [security.md](security.md) §1).

## Catalogue réel — 24 outils

### Système — 14 outils, tous `RequiresDaemon`

⚠️ `read_file`, `list_files` et `write_file` refusent tout tant que `Daemon:AllowedRoots` n'est
pas renseigné côté daemon (défaut fail-closed). L'écriture a son propre périmètre,
`Daemon:AllowedWriteRoots`, qui retombe sur le premier s'il est vide — lire et écrire ne sont pas
la même permission. Cf. [security.md](security.md).

| Outil | Destructif | Différable | Rôle |
|---|:---:|:---:|---|
| `get_system_status` | | | CPU, RAM, disque, uptime |
| `get_work_context` | | ❌ explicite | app au premier plan + fichier/projet ouvert (widget HUD permanent) |
| `git_status` | | | état du dépôt |
| `git_commit` | ✅ | ✅ | commit du travail en cours |
| `open_app` | | ✅ | lance une application |
| `open_browser_url` | | ✅ | ouvre une URL |
| `read_file` | | | lit un fichier — périmètre `AllowedRoots` ✅ |
| `write_file` | ✅ | ✅ | écrit un fichier — périmètre `AllowedWriteRoots` ✅ |
| `run_script` | ✅ | ❌ | PowerShell arbitraire — `-EncodedCommand`, plafond de durée |
| `list_files` | | | liste un répertoire — périmètre `AllowedRoots` ✅ |
| `kill_process` | ✅ | | termine un processus — refuse et énumère si le nom est ambigu |
| `clipboard` | ✅ | | lit / écrit le presse-papiers |
| `type_text` | ✅ | | frappe du texte au clavier |
| `capture_screen` | | | capture l'écran |

### Internet — 4 outils, aucun daemon

`web_search` · `web_fetch` · `web_browse` (Playwright) · `screenshot_page`

⚠️ **`web_fetch` et `web_browse` restent la porte d'entrée de l'injection de prompt** : ce
qu'ils rapportent entre dans le contexte du modèle. Le garde-fou correspondant n'est pas chez eux,
il est dans `IToolInvoker`.

Côté réseau, les trois outils qui vont chercher une page (`web_fetch`, `web_browse`,
`screenshot_page`) passent par **`UrlScope`** : schéma fermé à http/https, adresses internes
refusées après résolution DNS, `BlockedDomains` appliqué, redirections revalidées saut par saut.

### Mémoire — 6 outils, ORION se gère lui-même

`memory_save` · `memory_update` · `memory_forget` · `memory_reflect` (dimanche 23h) ·
`profile_update` · `proactive_feedback`

## Correspondance outil → action daemon

Les noms **ne coïncident pas** de part et d'autre du WebSocket, et c'est volontaire : chaque côté
définit ses propres types (pas de DLL partagée). Toute action doit figurer dans la whitelist de
`DaemonActionValidator` — sinon l'endpoint direct `/api/daemon/action` la refuse.

| Outil backend | Action daemon |
|---|---|
| `get_system_status` | `system_status` |
| `get_work_context` | `work_context` |
| `open_browser_url` | `open_url` |
| `clipboard` | `get_clipboard` / `set_clipboard` |
| *(les autres)* | même nom |

Actions daemon **sans outil backend** : `open_file`, `launch_claude`, `proactive_deferred`,
`speak`, `synthesize` (ces deux dernières sont locales au daemon, pas dans la whitelist).

## Créer un nouveau tool

1. Définir le contrat JSON dans `tools/definitions/{tool_name}.json`
2. Implémenter `ITool` dans `Orion.Business/Tools/{Catégorie}/{ToolName}Tool.cs`
3. **Enregistrer DEUX fois** dans `Program.cs` : `AddScoped<MonTool>()` puis
   `AddScoped<ITool>(sp => sp.GetRequiredService<MonTool>())` — sans la seconde ligne,
   `ToolRegistry` ne le découvre pas et le modèle ne le voit jamais
4. Si action système → implémenter aussi dans `daemon/Orion.Daemon.Actions/` **et** ajouter le nom
   à `DaemonActionValidator._allowedActions` (cf. [daemon.md](daemon.md))
5. **Trancher `IsDeferrable`** : cet outil garde-t-il un sens exécuté au réveil du PC ? Le test
   `ToolDeferrabilityTests` échoue tant que la réponse n'est pas inscrite dans sa liste
6. **Trancher `IsDestructive`** : écrit-il, supprime-t-il, exécute-t-il ? Dans le doute → `true`
7. **Trancher le PÉRIMÈTRE** : s'il touche au disque, au réseau interne ou aux processus, quelles
   racines / quels hôtes sont autorisés ? Le refus doit être le défaut — cf. [security.md](security.md) §5
8. Documenter dans `tools/definitions/README.md`
