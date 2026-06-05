# Frontend — PWA (surface unique, pas de pages)

ORION = organisme vivant, pas une app. Une seule surface, l'entité au centre.

**Pas de** : sidebar, bulles de chat (iMessage/ChatGPT), pages séparées, header/footer, input
permanent, boutons classiques.
**Oui à** : entité 3D vivante (respire/pulse/réagit), fond 3D animé (Three.js + particules), texte
qui émerge sous l'entité, données holographiques flottantes, input qui slide du bas au tap, voix
appui-long, mode light (#f9f8ff) / dark (#0d0d14).

```
Interactions : tap court=input · appui long=voix · tap ailleurs/Esc=ferme · swipe up=mémoire ·
               swipe down=briefing · double tap=settings · (gestes mains MediaPipe = Phase 5)
États entité : idle · listening · thinking · responding · daemon(flash blanc→violet 2s)
Stack 3D     : Three.js (@react-three/fiber + drei: Float/Billboard/Text3D) · Canvas API (particules
               2D) · Framer Motion · Web Audio API (amplitude→entité) · @mediapipe/hands (Phase 5)
```

## Structure

```
src/
├── algorithms/   vadProcessor, audioAnalyser, particleEngine, handTracker
├── components/   entity/ · hologram/ (HologramCard/Chart/ResponsePanel — pur Three.js GLSL/SDF) ·
│                 response/ · input/SlideInput · overlay/ (Memory/Briefing/Settings) ·
│                 canvas/ (Scene3D, OrionCore3D)
├── hooks/        useOrionEntity, useChat, useStream (appendChunk/setStreaming), useVAD,
│                 useVoiceWS (pipeline voix actif), useVoice (LEGACY HTTP), useGestures,
│                 useHandTracking, useOrionNotifications (SSE), usePushNotif, useOrionStatus
├── context/      EntityContext, OrionStatusContext, ThemeContext
├── services/     api.ts (axios) · chatService · memoryService · briefingService · daemonService ·
│                 healthService · voiceApi (LEGACY) · voiceWebSocket (client /ws/voice)
├── config/       endpoints.ts (ENDPOINTS centralisés + voiceWS: '/ws/voice')
├── types/        api/apiResponse · dto/ · models/
└── App.tsx       surface unique — pas de Router
```

`App.tsx` : `<ThemeProvider><EntityProvider>` → `<Scene3D>` (3D permanent z-0, reçoit responseText
+ isStreaming + onTap/onLongPress/onDoubleTap) + `<HoloCards>` (z-10) + `<SlideInput>` (slide bas) +
overlays Memory/Briefing/Settings (z-30) + statut points discrets (entité, VAD, daemon, SSE).

## Flow texte (chat clavier)

`SlideInput.onSubmit` → `App.handleSubmit` → `useStream.streamMessage` → `chatService.streamMessage`
(fetch SSE `POST /api/chat/stream`, parse lignes `data: …` jusqu'à `[DONE]`) → `setState.text` mis à
jour token par token → `useEffect` TTS Web Speech parle phrase par phrase (mode TEXT).

## Flow voix

Voir [voice.md](voice.md) — pipeline WebSocket full-duplex `/ws/voice`.

## Règles frontend

- axios via `api.ts` + `endpoints.ts` centralisé — **jamais `fetch` direct** (exception : SSE/stream
  car axios ne supporte pas `ReadableStream`)
- TypeScript strict — no `any`, no `as unknown`, types dans `src/types/`, props dans `src/props/`
- `npm run build` (tsc) doit passer : zéro variable/import non utilisé
- Hooks pour toute logique stateful ; composants reçoivent les données en props
