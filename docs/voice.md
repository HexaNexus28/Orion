# Voice Pipeline — Full-Duplex WebSocket `/ws/voice` (OPÉRATIONNEL)

```
FRONTEND (VAD)                          BACKEND (VoiceWebSocketHandler)
─────────────                           ──────────────────────────────
VAD détecte parole    ──PCM Int16──►    accumule _audioBuffer
end_audio (silence)   ──JSON───────►    Whisper.net STT (local)
                      ◄──JSON────       transcript
                      ◄──JSON────       llm_chunk (token/token, StreamLLMAsync)
                      ◄──JSON────       llm_done
AudioContext 24kHz    ◄──Binary───      Kokoro TTS (daemon) WAV raw bytes
barge-in (interrupt)  ──JSON───────►    CancellationToken annule le tour
```

## Protocole WebSocket

- **Client→Server** : Binary (PCM Int16 16kHz mono) | JSON `{type: config|end_audio|interrupt}`
- **Server→Client** : JSON `{type: ready|session|transcript|llm_start|llm_chunk|llm_done|tts_done|error|interrupted}` | Binary (WAV complets)
- **TTS daemon↔backend** : `[36-byte requestId UTF-8] + [raw WAV bytes]` (pas de base64)

## Stack

Whisper.net (STT local ~1.5GB) · KokoroSharp.CPU (TTS local ~320MB, voix `ff_siwis`) ·
@ricky0123/vad-web côté frontend — mais l'implémentation actuelle (`useVAD.ts`) utilise un VAD
maison par **RMS** (Web Audio `ScriptProcessorNode`) : seuil `0.015`, silence `700ms`, min parole `250ms`.

## Latence (Phase 4.5)

Prompt voix dédié (`ChatRequest.VoiceMode` → réponses courtes orales sans markdown) · smart sentence
split (`.!?` min 20 chars, weak break `, : ;` à 80, force à 150) · TTS pipeliné en parallèle du LLM
stream (`Task.WhenAll`) · frontend pre-decode chunk N+1 pendant lecture N (`useVoiceWS.ts`).

## Anti-écho — un seul chemin sonore, pas un réglage

**Web Speech API a été retirée d'ORION.** C'était elle, la cause.

Elle passe par le moteur TTS du **système**. Or un navigateur n'annule à l'écho que ce qu'**il**
joue lui-même : la voix d'ORION sortait du haut-parleur, rentrait par le micro, et aucun signal
de référence n'existait pour la distinguer de la parole de l'utilisateur. Sur un téléphone en
haut-parleur, sa voix est souvent **plus forte** que celle de l'utilisateur — le volume ne peut
donc rien trancher. Aucun seuil ne ferme ça.

Ce qui reste, une destination par chemin :

| | Chemin | Pourquoi |
|---|---|---|
| Conversation | WAV du WebSocket, joués par `AudioContext` | l'annulation d'écho du navigateur, elle, les voit |
| Proactif | action `speak` du daemon, haut-parleurs du PC | la notification naît des watchers du daemon : elle vient de la machine, elle sonne sur la machine — y compris application fermée |

**ORION parle quand on lui PARLE.** Le mode texte ne déclenche plus de synthèse : écris-lui, il
répond par écrit. C'est le prix assumé — il n'existait aucun moyen de faire parler le mode texte
dans le navigateur sans réintroduire le moteur système.

Gardes qui subsistent côté prise :

- une prise n'est ÉMISE que par `processVoiceTurn`, jamais par `onAudioChunk` : on n'envoie de
  l'audio que si on envoie aussi le `end_audio` qui le consomme ;
- une prise née pendant qu'ORION parle n'est gardée que si un barge-in a été **déclaré**.

## Flow frontend voix (App.tsx)

1. Mount → `useEffect([isInputVisible])` → `startPassiveListeningRef` → `startVAD()` (getUserMedia).
   ⚠️ L'`AudioContext` peut démarrer **suspended** jusqu'au 1er geste utilisateur → le VAD ne
   capte rien tant qu'on n'a pas tap/cliqué une fois.
2. `useVoiceWS` ouvre la connexion `/ws/voice` au mount (une seule fois, callbacks via ref).
3. VAD `onAudioChunk(pcm16)` → `sendAudioRef.current(pcm16)` → `ws.sendAudio` (streaming temps réel
   pendant la parole uniquement).
4. VAD détecte fin de parole → `processVoiceTurn()` → `endAudio()` (JSON `end_audio`).
5. Backend : STT → `PrepareStreamAsync` → `StreamLLMAsync` → TTS phrase par phrase.
6. Callbacks WS : `onTranscript` (reset + thinking) → `onLLMChunk` (`appendChunk`) →
   `onLLMDone` → `onOrionSpeaking(true/false)` pilote l'état entité + `isTTSSpeaking`.
7. Barge-in : si `isSpeaking && isTurnActive && amplitude > 0.04` → `interrupt()`.

## Points de vigilance connus

- AudioContext suspended au 1er chargement (cf. point 1) — un tap initial débloque.
- Pendant la lecture TTS, le VAD continue de streamer du PCM au backend (pas de gating sur
  `isTurnActive` dans `sendAudioRef`) → l'écho peut s'accumuler dans `_audioBuffer` côté serveur.
- `npm run build` (tsc strict) doit passer — pas de variables non utilisées.
