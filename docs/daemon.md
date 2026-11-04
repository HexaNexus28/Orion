# Daemon Windows

Worker Service .NET 9 qui exécute les actions sur le poste. Il initie une connexion WSS vers le
backend, s'authentifie avec `X-Daemon-Token` et se reconnecte avec un délai exponentiel.

## Projets

| Projet | Contenu |
|---|---|
| `Orion.Daemon` | `DaemonWorker`, WebSocket, watchers, notificateurs, `ProactiveOrchestrator`, actions `speak` et `synthesize` |
| `Orion.Daemon.Core` | entités, interfaces, configuration, `PathScope` |
| `Orion.Daemon.Actions` | actions système et `ActionRegistry` |
| `Orion.Daemon.Tests` | tests |

Notificateurs : `WindowsToastNotifier`, `WindowsNotifier`, `PowerShellTtsNotifier`, `KokoroSpeaker`.

## Installation

```powershell
powershell -File scripts\install-daemon.ps1
```

Si Windows bloque le script :

```powershell
Unblock-File scripts\install-daemon.ps1
```

Le script publie le daemon, copie les voix, conserve la configuration et relance le processus. Il
est à rejouer après chaque modification du code du daemon. Il ne demande pas d'élévation.

Le crochet `.githooks/post-merge` signale un `git pull` qui touche `daemon/`.

### Exécution dans la session utilisateur

Le daemon est lancé par le dossier Démarrage. Un service Windows s'exécuterait en session 0, sans
accès au bureau ni au son : notifications et voix locale ne fonctionneraient pas, et `open_app`
ouvrirait des applications invisibles. En contrepartie, le daemon ne tourne pas quand la session est
fermée.

### Fichiers Kokoro

`dotnet publish` n'inclut pas `voices/` ni `espeak/`. Le script d'installation les copie. Leurs
chemins sont relatifs : le lanceur fixe donc le répertoire de travail.

## Actions

Le backend vérifie les noms d'actions avec `DaemonActionValidator`. Cette liste ne filtre pas les
arguments : chaque action valide ses entrées.

Le daemon traite les commandes l'une après l'autre.

## Périmètre disque

`read_file`, `list_files` et `write_file` passent par `PathScope`. Sans racine déclarée, ils refusent
tout.

```json
{
  "Daemon": {
    "AllowedRoots": ["C:\\chemin\\vers\\depots"],
    "AllowedWriteRoots": [],
    "DeniedNames": []
  }
}
```

| Clé | Rôle |
|---|---|
| `AllowedRoots` | racines lisibles |
| `AllowedWriteRoots` | racines modifiables ; si la liste est vide, `AllowedRoots` s'applique |
| `DeniedNames` | noms refusés même sous une racine autorisée ; vide, la liste par défaut s'applique |
| `ScriptTimeoutSeconds` | durée maximale de `run_script`, 120 s par défaut |

Déclarer des racines étroites. Ne pas déclarer le profil utilisateur entier : il contient des clés,
des cookies et des jetons.

## Configuration

`%LOCALAPPDATA%\Orion\daemon\appsettings.Production.json`, hors du dépôt :

```json
{
  "Daemon": {
    "RenderWsUrl": "wss://example.com/daemon",
    "Token": "<valeur de DAEMON_WS_TOKEN>",
    "MachineName": "<nom-de-la-machine>",
    "ReconnectDelayMs": 5000,
    "MaxReconnectDelayMs": 60000
  }
}
```

`RenderWsUrl` contient l'URL WebSocket du backend, quel que soit l'hébergeur.

## Vérification

```powershell
Get-Process Orion.Daemon
```

Côté serveur, la connexion apparaît dans les logs sous la forme `Daemon connected from <machine>`.
