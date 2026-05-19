---
description: Voice pipeline full-duplex — architecture, endpoints, et workflow
---

# Voice Pipeline — Full-Duplex WebSocket

## Architecture Générale

```
┌─────────────────────────────────────────────────────────────┐
│                      FRONTEND                                │
│                                                              │
│  ┌─────────┐   PCM int16 chunks    ┌──────────────────────┐ │
│  │  useVAD  │ ─────────────────────→│  VoiceWebSocket      │ │
│  │  (RMS)   │   endAudio/interrupt  │  ws://host/ws/voice  │ │
│  └────┬─────┘ ─────────────────────→│                      │ │
│       │ onSpeechStart               │  ← transcript (JSON) │ │
│       │ onSpeechEnd                 │  ← llm_chunk (JSON)  │ │
│       │                             │  ← WAV audio (bin)   │ │
│       ▼                             └──────────┬───────────┘ │
│  ┌──────────┐                                  │             │
│  │ Entity   │◄─ state: idle/listening/          │             │
│  │ (3D orb) │   thinking/responding             │             │
│  └──────────┘                                  │             │
│  ┌──────────────┐                              │             │
│  │ AudioContext  │◄─── WAV chunks (24kHz) ─────┘             │
│  │ (playback)   │     sequential, gapless                    │
│  └──────────────┘                                            │
│  ┌──────────────┐                                            │
│  │ ResponseText  │◄─── llm_chunk (streaming markdown)        │
│  └──────────────┘                                            │
└─────────────────────────────────────────────────────────────┘
                           ▲ WebSocket ▼
┌─────────────────────────────────────────────────────────────┐
│                      BACKEND (Orion.Api)                      │
│                                                              │
│  VoiceWebSocketMiddleware → VoiceWebSocketHandler            │
│                                                              │
│  1. Reçoit PCM audio chunks → accumule dans _audioBuffer     │
│  2. Reçoit "end_audio" → ProcessTurnAsync()                  │
│  3. STT (Whisper.net) → transcript                           │
│  4. LLM stream (ConversationAgent) → chunks texte            │
│  5. TTS par phrase (VoiceNotificationService → Daemon WS)    │
│  6. Envoie WAV binaire au frontend                           │
│  7. Si "interrupt" → CancellationToken.Cancel()              │
│                                                              │
│  Session: Guid persistant pour multi-tour                    │
└─────────────────────────┬───────────────────────────────────┘
                          │ WebSocket (IDaemonClient)
┌─────────────────────────▼───────────────────────────────────┐
│                      DAEMON (Orion.Daemon)                    │
│                                                              │
│  KokoroSpeaker (KokoroSharp.CPU)                             │
│  - Action "synthesize" → texte → WAV base64                 │
│  - Voix: ff_siwis (French female)                            │
│  - Modèle auto-download ~320MB                               │
│  - espeak-ng pour phonémisation                              │
└─────────────────────────────────────────────────────────────┘
```

## Endpoints

### WebSocket (Primary — Full-Duplex)

| Endpoint | Transport | Description |
|----------|-----------|-------------|
| `/ws/voice` | WebSocket | Pipeline voix complet bidirectionnel |

### HTTP REST (Legacy + Utilities)

| Endpoint | Méthode | Description |
|----------|---------|-------------|
| `/api/voice/transcribe` | POST | STT seul (Whisper) |
| `/api/voice/synthesize` | POST | TTS seul (Kokoro via daemon) |
| `/api/voice/status` | GET | État Whisper + Kokoro |
| `/api/voice/converse` | POST | Legacy half-duplex (remplacé par WS) |
| `/api/chat/stream` | POST | Chat texte streaming (SSE) |
| `/api/proactivenotification/stream` | GET | SSE notifications daemon → frontend |
| `/daemon` | WebSocket | Connexion daemon → backend |

## Protocole WebSocket `/ws/voice`

### Client → Server

| Type | Format | Description |
|------|--------|-------------|
| Audio | Binary (Int16 PCM 16kHz) | Chunks audio en temps réel pendant la parole |
| `end_audio` | JSON `{"type":"end_audio"}` | Fin de parole → déclenche STT → LLM → TTS |
| `interrupt` | JSON `{"type":"interrupt"}` | Barge-in : annule le turn en cours |
| `config` | JSON `{"type":"config","language":"fr","sessionId":"..."}` | Configuration session |

### Server → Client

| Type | Format | Description |
|------|--------|-------------|
| `ready` | JSON | Serveur prêt |
| `session` | JSON `{"type":"session","id":"..."}` | Session ID pour multi-tour |
| `transcript` | JSON `{"type":"transcript","text":"..."}` | Résultat STT |
| `llm_start` | JSON | Début du streaming LLM |
| `llm_chunk` | JSON `{"type":"llm_chunk","text":"..."}` | Token LLM (pour affichage texte) |
| `llm_done` | JSON `{"type":"llm_done","text":"..."}` | Texte LLM complet |
| Audio WAV | Binary | Chunk WAV complet (jouer immédiatement) |
| `tts_done` | JSON | Fin de tous les chunks audio |
| `interrupted` | JSON | Turn annulé (barge-in confirmé) |
| `error` | JSON `{"type":"error","message":"..."}` | Erreur |

## Workflow Détaillé — Un Tour de Parole

```
                FRONTEND                        BACKEND                        DAEMON
                ────────                        ───────                        ──────
1. VAD détecte parole
   → setState('listening')
   → onAudioChunk(Int16)     ─── binary PCM ──→ _audioBuffer.Add()
   → onAudioChunk(Int16)     ─── binary PCM ──→ _audioBuffer.Add()
   → ...                                        ...

2. VAD détecte 700ms silence
   → finalizeSpeech()
   → processVoiceTurn()
   → endAudio()              ── {"type":"end_audio"} ──→ ProcessTurnAsync()
   → setState('thinking')

3.                                               EncodePcmToWav()
                                                 WhisperService.TranscribeAsync()
                             ←── {"type":"transcript","text":"Bonjour"} ──

4.                                               ConversationAgent.StreamAsync()
                             ←── {"type":"llm_start"} ──
                             ←── {"type":"llm_chunk","text":"Sal"} ──
                             ←── {"type":"llm_chunk","text":"ut"} ──
                             ←── {"type":"llm_chunk","text":" !"} ──
   → setState('responding')
   → display text chunks

5.                                               Sentence complete → TTS
                                                 VoiceNotificationService
                                                   .SynthesizeAsync("Salut !")
                                                                     ── WS "synthesize" ──→ KokoroSpeaker
                                                                     ←── base64 WAV ──────
                             ←── binary WAV ──
   → AudioContext.decodeAudioData()
   → play audio 🔊

6.                                               More LLM chunks...
                             ←── {"type":"llm_done","text":"Salut ! Comment..."} ──
                             ←── {"type":"tts_done"} ──
   → wait audio queue empty
   → setState('idle')
   → VAD reprend l'écoute

── BARGE-IN ──────────────────────────────────────────────────────────
   VAD détecte parole pendant responding
   → interrupt()             ── {"type":"interrupt"} ──→ _turnCts.Cancel()
   → stopPlayback()                                      (LLM + TTS annulés)
   → setState('listening')   ←── {"type":"interrupted"} ──
   → nouveau tour commence...
```

## Fichiers Clés

### Backend
- `Orion.Api/WebSockets/VoiceWebSocketHandler.cs` — Handler principal
- `Orion.Api/WebSockets/VoiceWebSocketMiddleware.cs` — Route /ws/voice
- `Orion.Api/Controllers/VoiceController.cs` — Endpoints HTTP legacy
- `Orion.Business/Services/VoiceNotificationService.cs` — TTS via daemon
- `Orion.Business/Services/WhisperService.cs` — STT Whisper.net
- `Orion.Business/Agents/ConversationAgent.cs` — LLM streaming

### Frontend
- `src/services/voiceWebSocket.ts` — Client WebSocket typé
- `src/hooks/useVoiceWS.ts` — Hook React (playback + state)
- `src/hooks/useVAD.ts` — VAD amplitude + streaming PCM
- `src/App.tsx` — Orchestration (barge-in, états entité)

### Daemon
- `Orion.Daemon/Notifiers/KokoroSpeaker.cs` — TTS KokoroSharp

## Paramètres Ajustables

| Paramètre | Fichier | Valeur | Description |
|-----------|---------|--------|-------------|
| `SILENCE_TIMEOUT_MS` | useVAD.ts | 700ms | Durée silence avant fin de parole |
| `SPEECH_THRESHOLD` | useVAD.ts | 0.015 | Seuil RMS pour détecter la parole |
| `MIN_SPEECH_MS` | useVAD.ts | 250ms | Durée min pour éviter faux positifs |
| `sampleRate` | useVoiceWS.ts | 24000 | Sample rate playback AudioContext |
| TTS timeout | VoiceNotificationService.cs | 8s | Timeout synthèse Kokoro |
| WS KeepAlive | Program.cs | 30s | Interval ping WebSocket |
| Whisper model | WhisperService.cs | ggml-small.bin | Modèle STT |
