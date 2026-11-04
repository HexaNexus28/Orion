# Proactivité

Le daemon observe l'activité du poste et décide s'il faut interrompre l'utilisateur.

```
Watchers → PatternDetected → IProactiveDecider → message LLM → notification
```

## Watchers

| Watcher | Observation |
|---|---|
| `ActivityWatcher` | inactivité et durée de travail |
| `TimeWatcher` | moments de la journée |
| `SystemWatcher` | charge CPU et mémoire |
| `ProcessWatcher` | applications au premier plan |
| `AdaptiveWatcher` | routines apprises |
| `WorkWatcher` | état du travail, dont les commits non poussés et le retour d'un service |

## Décision

`ProactiveOrchestrator` reçoit les signaux et les soumet à `IProactiveDecider`, qui décide de les
émettre, de les différer ou de les écarter. Les signaux différés sont récupérés ensuite par
`RecupererDifferes`.

`ProactiveLearningService` calcule des pénalités à partir des retours de l'utilisateur.
L'orchestrateur les relit depuis `GET /api/proactivenotification/weights`. L'outil
`proactive_feedback` enregistre ces retours.

## Diffusion

| Canal | Condition |
|---|---|
| SSE vers la PWA | client connecté |
| Notification Windows | daemon actif |
| Voix locale | action `speak`, haut-parleurs du poste |

Si le LLM est indisponible, l'orchestrateur utilise un message de repli.

## API

| Route | Rôle |
|---|---|
| `GET /api/proactivenotification/stream` | flux SSE |
| `POST /api/proactivenotification/notify` | diffuse une notification |
| `POST /api/proactivenotification/trigger` | génère un message à partir d'un signal |
| `GET /api/proactivenotification/weights` | pénalités apprises |
| `POST /api/proactivenotification/action` | transmet une action au daemon |
