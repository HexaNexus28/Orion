# Frontend

PWA React à surface unique : pas de routeur ni de pages. Une scène 3D occupe l'écran et les autres
éléments s'y superposent.

## Interactions

| Geste | Effet |
|---|---|
| tap | ouvre la saisie |
| appui long | déclenche un tour vocal |
| double tap | ouvre les réglages |
| glissement vers le haut | ouvre la mémoire |
| glissement vers le bas | ouvre le briefing |
| `Échap` ou tap extérieur | ferme la surface |
| touches `M` et `B` | ouvrent la mémoire et le briefing |

La pastille `DeferredQueueBadge`, en haut à droite, apparaît quand des actions sont en attente. Elle
ouvre le panneau qui permet de les confirmer ou de les annuler.

États de l'entité : `idle`, `listening`, `thinking`, `responding`.

## Structure

```
src/
  algorithms/  détection des mains
  components/  auth, canvas, hologram, input, overlay, ui
  config/      endpoints.ts
  context/     EntityContext, OrionStatusContext, HudCardsContext
  hooks/       useStream, useVAD, useVoiceWS, useGestureControl,
               useHandTracking, useOrionNotifications
  props/       props de composants
  services/    api, authService, chatService, memoryService, toolService,
               briefingService, daemonService, deferredService,
               healthService, voiceWebSocket
  types/       api, dto, models
  utils/       animationUtils
  App.tsx      surface unique
```

`main.tsx` empile `AuthGate`, `EntityProvider`, `OrionStatusProvider`, `HudCardsProvider`, puis
`App`. Sans session, `App` n'est pas monté.

## Couches d'affichage

| Plan | Contenu |
|---|---|
| `z-0` | scène 3D |
| `z-10` | état vocal |
| `z-20` | boutons, pastille, outils, notifications |
| `z-30` | surfaces modales, une seule ouverte à la fois |
| `z-40` | voile de la saisie |
| `z-50` | saisie et messages bloquants |

## Flux texte

`SlideInput` → `App.handleSubmit` → `useStream.streamMessage` → `POST /api/chat/stream` (SSE).
La réponse s'affiche au fil de l'eau. Le mode texte ne parle pas.

## Flux vocal

Voir [voice.md](voice.md).

## File différée

`useDeferredQueue` est appelé une seule fois dans `App`, et son résultat alimente la pastille et le
panneau. La file se recharge au retour du daemon et à la notification SSE `deferred`.

## Règles

- Appels HTTP via `api.ts` et `endpoints.ts`, sauf flux SSE.
- TypeScript strict, sans `any` ni `as unknown`.
- Logique avec état dans des hooks.
- `npm run build` doit passer.
