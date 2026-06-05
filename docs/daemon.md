# Daemon — Agent Système Windows

Worker Service .NET 9 (PAS ASP.NET). Tourne 24/7, **initie** la connexion WSS vers le backend
(sens daemon→backend : pas de souci firewall/IP dynamique). Auth header `X-Daemon-Token`, heartbeat
bidirectionnel, reconnexion backoff exponentiel.

## Structure — 3 projets

```
Orion.Daemon/         Workers/DaemonWorker · WebSocket (Manager + MessageHandler) ·
                      Watchers (Activity, Time, Process, System, Adaptive) ·
                      Notifiers (WindowsToast, Windows[MessageBox], PowerShellTts[SAPI5],
                      Kokoro[KokoroSharp.CPU v0.6.6, voix ff_siwis]) · ProactiveOrchestrator
Orion.Daemon.Core/    Entities (DaemonCommand/Response) · Interfaces (IAction, IActionRegistry) ·
                      Configuration/DaemonOptions
Orion.Daemon.Actions/ ActionRegistry + OpenApp, OpenFileInEditor, RunScript, LaunchClaude,
                      OpenBrowserUrl, GetSystemStatus, ReadFile, WriteFile, GitStatus, GitCommit
```

## Flux proactif

```
Watcher détecte pattern (ex: inactif 3h + skip_meal)
  → POST /api/proactivenotification/notify (ou WS actif)
  → backend stocke + broadcast SSE
  → frontend EventSource → TTS
  → (ou) notification Windows native + TTS daemon, sans ouvrir l'app
```

Toute action doit être dans la **whitelist** avant implémentation.

## Installation

```powershell
cd orion/daemon
dotnet publish Orion.Daemon -c Release -r win-x64 --self-contained -o C:\orion\daemon
sc create OrionDaemon binPath="C:\orion\daemon\Orion.Daemon.exe" start=auto
sc start OrionDaemon
sc query OrionDaemon   # vérifier STATE: RUNNING
```

## Config Production

```json
{ "Daemon": {
  "RenderWsUrl": "wss://orion-api.onrender.com/daemon",
  "Token": "same-secret-as-render-env",
  "ReconnectDelayMs": 5000,
  "MaxReconnectDelayMs": 60000
}}
```
