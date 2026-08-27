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
Orion.Daemon.Actions/ ActionRegistry + 18 actions : OpenApp, OpenFileInEditor, RunScript,
                      LaunchClaude, OpenBrowserUrl, GetSystemStatus, GetWorkContext, ReadFile,
                      WriteFile, ListFiles, GitStatus, GitCommit, KillProcess, GetClipboard,
                      SetClipboard, TypeText, CaptureScreen, ProactiveDeferred
                      (+ Speak / Synthesize, locales au projet Orion.Daemon)
```

## Flux proactif

```
Watcher détecte pattern (ex: inactif 3h + skip_meal)
  → POST /api/proactivenotification/notify (ou WS actif)
  → backend stocke + broadcast SSE
  → frontend EventSource → TTS
  → (ou) notification Windows native + TTS daemon, sans ouvrir l'app
```

Toute action doit être dans la **whitelist** (`DaemonActionValidator`) avant implémentation.

⚠️ **La whitelist ne filtre que le NOM de l'action, jamais ses arguments.** Un chemin de fichier ou
une URL la traverse sans être examiné. Le **périmètre** est donc la responsabilité de l'action
elle-même.

`ReadFile` et `ListFiles` le portent depuis le 2026-08-27 via
`Orion.Daemon.Core/Security/PathScope.cs`. `WriteFile` **ne l'a pas encore** — il reste protégé par
la seule confirmation (constat C2 ouvert). Lire [security.md](security.md) avant d'ajouter une
action qui touche au disque.

## Installation

```powershell
powershell -File scripts\install-daemon.ps1
```

Si Windows bloque le script (marque « téléchargé depuis Internet »), préférer `Unblock-File` à un
contournement global de la politique d'exécution :

```powershell
Unblock-File scripts\install-daemon.ps1
```

A rejouer **a chaque changement du code du daemon**. Le script publie, transporte les voix,
conserve la configuration et relance. Aucune elevation requise.

### Pourquoi PAS un service Windows

Le daemon sait tourner en service (`AddWindowsService` est present), mais un service vit en
**session 0**, isolee du bureau depuis Windows Vista. Or ses quatre notificateurs dependent tous
de la session utilisateur :

| Notificateur | Ce qu il fait | En session 0 |
|---|---|---|
| `PowerShellTtsNotifier` | `SpeechSynthesizer.Speak()` | aucun peripherique audio |
| `KokoroSpeaker` | joue un WAV | aucun peripherique audio |
| `WindowsToastNotifier` | toast Windows | invisible |
| `WindowsNotifier` | boite de dialogue | invisible |

Installe en service, ORION serait **muet et invisible**, et `OpenApp` ouvrirait des applications
sur un bureau que personne ne voit. Le tuyau fonctionnerait, le Jarvis serait mort.

D ou le lancement par le **dossier Demarrage**, dans la session utilisateur. Contrepartie
assumee : rien ne tourne quand la session est fermee — mais il n y a alors personne a qui parler.

Une tache planifiee aurait ajoute le redemarrage automatique en cas de plantage du processus ;
sa creation exige un administrateur sur ce poste. Les coupures RESEAU, elles, sont deja gerees
par le daemon (reconnexion a palier exponentiel).

### Le piege des voix Kokoro

`dotnet publish` **n emporte pas** `voices/` ni `espeak/` : KokoroSharp les telecharge a
l execution, dans le repertoire de travail. Sans elles le daemon demarre, se connecte, parait
sain — et reste muet (`GetVoice` leve `DirectoryNotFoundException`, seule la voix Windows de
secours repond). Le script les transporte depuis la sortie de build ; c est l etape que le
`dotnet publish` de l ancienne documentation laissait tomber en silence.

Pour la meme raison, le lanceur `demarrer-orion.vbs` fixe `CurrentDirectory` : ces chemins sont
**relatifs**. Lance depuis ailleurs, ORION est muet.

## Périmètre de lecture — `AllowedRoots`

`read_file` et `list_files` sont **fail-closed** : sans racine déclarée, ils refusent tout. Ce
n'est pas une panne — c'est le défaut voulu, et le message d'erreur nomme la clé à renseigner.

```json
{ "Daemon": {
  "AllowedRoots": [
    "C:\\chemin\\vers\\tes\\depots",
    "C:\\Users\\<toi>\\Documents"
  ],
  "DeniedNames": []
}}
```

À déclarer **étroit**. Surtout pas `C:\Users\<toi>` : le profil contient `.ssh`, les bases de
cookies et les jetons. Une racine autorisée dit où ORION a le droit de chercher — et ce qu'il y
lit repart dans sa réponse, donc hors de la machine.

`DeniedNames` vide = liste par défaut (`.ssh`, `.aws`, `.git`, `id_rsa`, `.env*`,
`appsettings.Production.json`, …), refusée **même sous une racine autorisée** : un dépôt de code
légitime contient presque toujours un `.env`.

## Config Production

`%LOCALAPPDATA%Oriondaemonappsettings.Production.json` — **hors du depot**, jamais versionne.

```json
{ "Daemon": {
  "RenderWsUrl": "wss://<ton-domaine>/daemon",
  "Token": "<identique a DAEMON_WS_TOKEN cote serveur>",
  "MachineName": "<nom-de-ta-machine>",
  "ReconnectDelayMs": 5000,
  "MaxReconnectDelayMs": 60000
}}
```

Le nom `RenderWsUrl` est un vestige : l hebergement est le VPS, plus Render.

Le jeton est **fail-closed** cote serveur depuis le 2026-08-26 : vide ou absent, la connexion est
REFUSEE (avant, un jeton non configure faisait sauter le controle). `appsettings.json` du projet
porte volontairement un jeton VIDE — le vrai ne vit que dans le dossier d installation.

## Verifier que tout marche

```powershell
Get-Process Orion.Daemon                     # doit exister, sans fenetre
```

Cote serveur, la preuve reelle :

```bash
docker logs orion --since 2m | grep -E "Daemon connected|weights"
#   Daemon connected from <nom-de-ta-machine>
#   GET /api/proactivenotification/weights - 200      <- le mode proactif fonctionne
```
