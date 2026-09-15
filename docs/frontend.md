# Frontend — PWA (surface unique, pas de pages)

ORION = organisme vivant, pas une app. Une seule surface, l'entité au centre.

**Pas de** : sidebar, bulles de chat (iMessage/ChatGPT), pages séparées, header/footer, input
permanent, boutons classiques.
**Oui à** : entité 3D vivante (respire/pulse/réagit), fond 3D animé (Three.js + particules), texte
qui émerge sous l'entité, données holographiques flottantes, input qui slide du bas au tap, voix
appui-long, mode light (#f9f8ff) / dark (#0d0d14).

```
Interactions : tap court=input · appui long=voix · tap ailleurs/Esc=ferme · swipe up=mémoire ·
               swipe down=briefing · double tap=settings · pastille haut-droite=file différée
               (n'apparaît QUE si quelque chose attend) · (gestes mains MediaPipe = Phase 5)
États entité : idle · listening · thinking · responding · daemon(flash blanc→violet 2s)
Stack 3D     : Three.js (@react-three/fiber + drei: Float/Billboard/Text3D) · Canvas API (particules
               2D) · Framer Motion · Web Audio API (amplitude→entité) · @mediapipe/hands (Phase 5)
```

## Structure

⚠️ Ce bloc est vérifié contre le dépôt. Un fichier cité ici et introuvable est un bug de la doc,
pas un fichier à recréer.

```
src/
├── algorithms/   handTracker (MediaPipe, Phase 5)
├── components/   auth/ (AuthGate, LoginScreen) · canvas/ (Scene3D, OrionCore3D) ·
│                 hologram/ (HologramCard/Chart/Text/ResponsePanel/ResponseText3D — Three.js GLSL/SDF) ·
│                 input/SlideInput (CLAVIER seul) · ui/HudZones ·
│                 overlay/ (Memory, Briefing, Settings, DeferredQueue+Badge, ToolActivityStrip,
│                 VoiceStatusHint)
├── hooks/        useStream (SSE texte), useVAD (Silero v5), useVoiceWS (SEUL pipeline voix),
│                 useGestureControl, useHandTracking, useOrionNotifications (SSE proactif)
├── context/      EntityContext, OrionStatusContext, HudCardsContext
├── props/        props de composants — jamais inline
├── services/     api.ts (axios) · authService · chatService · memoryService · toolService ·
│                 briefingService · daemonService · deferredService · healthService ·
│                 voiceWebSocket (client /ws/voice)
├── config/       endpoints.ts (ENDPOINTS centralisés + voiceWS: '/ws/voice')
├── types/        api/apiResponse · dto/ · models/
├── utils/        animationUtils
└── App.tsx       surface unique — pas de Router, pas de pages/
```

`main.tsx` : `<AuthGate>` → `<EntityProvider>` → `<OrionStatusProvider>` → `<HudCardsProvider>` →
`<App/>`. `AuthGate` est une BARRIÈRE, pas un habillage : sans session, App n'est jamais monté.

`App.tsx` : `<Scene3D>` (3D permanent z-0, reçoit responseText + isStreaming +
onTap/onLongPress/onDoubleTap) + `<HudZones>` (z-10) + `<SlideInput>` (slide bas) +
overlays Memory/Briefing/Settings/DeferredQueue (z-30, **exclusifs** — un seul `activeOverlay` à la
fois) + statut points discrets (entité, VAD, daemon, SSE) + `DeferredQueueBadge` (z-20).

**File d'actions différées** — `useDeferredQueue` est appelé **une seule fois**, dans `App`, et son
résultat descend vers la pastille ET l'overlay : deux instances feraient deux appels, et surtout la
pastille afficherait un compteur périmé après une confirmation faite dans l'overlay. La file se
relit sur deux signaux — la reconnexion du daemon (immédiat) et la notification SSE `deferred`
(émise APRÈS le drain, donc celle qui fait autorité sur l'état réel.)

## Flow texte (chat clavier)

`SlideInput.onSubmit` → `App.handleSubmit` → `useStream.streamMessage` → `chatService.streamMessage`
(fetch SSE `POST /api/chat/stream`, parse lignes `data: …` jusqu'à `[DONE]`) → `setState.text` mis à
jour token par token → affichage, **et rien d'autre**.

Le mode texte ne parle pas. Un `useEffect` faisait auparavant lire la réponse phrase par phrase par
`speechSynthesis` : ORION répondait à voix haute à une question tapée au clavier, et cette voix-là
sortait du moteur du système — donc hors de portée de l'annulation d'écho du navigateur, qui n'annule
que ce qu'il joue lui-même. Le micro se réentendait. Web Speech a été retirée : **on écrit, ORION
écrit ; on parle, ORION parle** (cf. [voice.md](voice.md)).

## Flow voix

Voir [voice.md](voice.md) — pipeline WebSocket full-duplex `/ws/voice`.

## Règles frontend

- axios via `api.ts` + `endpoints.ts` centralisé — **jamais `fetch` direct** (exception : SSE/stream
  car axios ne supporte pas `ReadableStream`)
- TypeScript strict — no `any`, no `as unknown`, types dans `src/types/`, props dans `src/props/`
- `npm run build` (tsc) doit passer : zéro variable/import non utilisé
- Hooks pour toute logique stateful ; composants reçoivent les données en props
