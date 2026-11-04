# Outils

`ToolRegistry` expose 24 outils au modèle.

## Exécution

```
Modèle → AgentLoop → tool_call ─┐
                                ├─► IToolInvoker → exécute, diffère ou refuse
HUD → POST /api/tools/{name} ───┘
```

Le modèle et le HUD empruntent le même `IToolInvoker`, donc les mêmes contrôles.

| Situation | Résultat |
|---|---|
| outil sans daemon, non destructif | exécution |
| daemon requis, poste éteint, outil différable | mise en file |
| daemon requis, poste éteint, outil non différable | refus expliqué |
| outil destructif | mise en attente de confirmation |

Au retour du poste, les actions destructives passent en attente de confirmation et les autres
s'exécutent.

## Membres de `ITool`

| Membre | Défaut | Signification |
|---|---|---|
| `RequiresDaemon` | `false` | passe par le poste de l'utilisateur |
| `IsDestructive` | `false` | écrit, supprime ou exécute : confirmation exigée |
| `IsDeferrable` | `false` | garde un sens s'il est exécuté plus tard |
| `BuildCard(result)` | `null` | carte du HUD produite à partir du résultat |

`ToolDeferrabilityTests` échoue tant qu'un nouvel outil daemon n'a pas été classé.

## Catalogue

### Système

| Outil | Destructif | Différable | Rôle |
|---|:---:|:---:|---|
| `get_system_status` | | | CPU, RAM, disque, durée de fonctionnement |
| `get_work_context` | | | application au premier plan et fichier ouvert |
| `git_status` | | | état du dépôt |
| `git_commit` | oui | oui | commit du travail en cours |
| `open_app` | | oui | lance une application |
| `open_browser_url` | | oui | ouvre une URL |
| `read_file` | | | lit un fichier dans `AllowedRoots` |
| `write_file` | oui | oui | écrit un fichier dans `AllowedWriteRoots` |
| `list_files` | | | liste un répertoire dans `AllowedRoots` |
| `run_script` | oui | | exécute un script PowerShell |
| `kill_process` | oui | | termine un processus |
| `clipboard` | oui | | lit ou écrit le presse-papiers |
| `type_text` | oui | | saisit du texte au clavier |
| `capture_screen` | | | capture l'écran |

`run_script` n'est pas différable : un script étant arbitraire, son utilité différée ne peut pas
être évaluée.

`kill_process` refuse un nom ambigu et liste les processus correspondants. Le modèle vise alors un
`pid`, ou demande explicitement `all: true`.

### Internet

`web_search`, `web_fetch`, `web_browse` (Playwright), `screenshot_page`.

`web_fetch`, `web_browse` et `screenshot_page` passent par `UrlScope`.

### Mémoire

`memory_save`, `memory_update`, `memory_forget`, `memory_reflect`, `profile_update`,
`proactive_feedback`.

## Correspondance avec les actions du daemon

| Outil | Action daemon |
|---|---|
| `get_system_status` | `system_status` |
| `get_work_context` | `work_context` |
| `open_browser_url` | `open_url` |
| `clipboard` | `get_clipboard`, `set_clipboard` |
| autres outils système | même nom |

Actions sans outil associé : `open_file`, `launch_claude`, `proactive_deferred`, `speak`,
`synthesize`.

## Créer un outil

1. Implémenter `ITool` dans `Orion.Business/Tools/{Catégorie}/`.
2. Enregistrer le type concret, puis `ITool`, dans `Program.cs` :

   ```csharp
   builder.Services.AddScoped<MyTool>();
   builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<MyTool>());
   ```

3. Pour une action système, l'implémenter dans `Orion.Daemon.Actions` et ajouter son nom à
   `DaemonActionValidator`.
4. Décider `IsDeferrable` et l'inscrire dans `ToolDeferrabilityTests`.
5. Décider `IsDestructive`. En cas de doute, choisir `true`.
6. Définir le périmètre si l'outil touche au disque, au réseau interne ou aux processus.
7. Mettre à jour ce document.
