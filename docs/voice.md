# Voice Pipeline — Full-Duplex WebSocket `/ws/voice` (OPÉRATIONNEL)

```
FRONTEND (VAD)                          BACKEND (VoiceWebSocketHandler)
─────────────                           ──────────────────────────────
VAD détecte parole    ──PCM Int16──►    accumule _audioBuffer
end_audio (silence)   ──JSON───────►    TranscriptionCascade (Voxtral, repli Whisper)
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

STT : `TranscriptionCascade` — Voxtral (Mistral) en tête, Whisper.net local en repli ·
KokoroSharp.CPU (TTS local ~320MB, voix `ff_siwis`) · VAD : `MicVAD` Silero v5 (`useVAD.ts`) —
silence de clôture ≈450 ms, parole minimale ≈290 ms, pré-roll ≈256 ms.

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
- une prise née pendant qu'ORION parle n'est gardée que si un barge-in a été **déclaré** ;
- `isPlayingRef` reste vrai jusqu'au `onended` du dernier son : programmer un son rend la main
  aussitôt, seul `onended` dit qu'il s'est tu. Couper ORION en pleine phrase n'est donc **pas**
  possible : ce que le micro capte pendant la lecture est traité comme de l'écho.

## Phrases coupées par une hésitation

La prise se clôt après ≈450 ms de silence. Si la parole reprend dans les 3 s
(`PHRASE_RESUME_WINDOW_MS`) et qu'ORION n'a ni lancé d'outil ni fini de répondre, le tour en cours
est annulé et le serveur reçoit les deux prises recollées. Un bruit bref (`onMisfire`) renvoie la
prise d'origine telle quelle. L'envoi reste immédiat : aucune latence ajoutée.

⚠️ Le message utilisateur est écrit dès `PrepareStreamAsync` : un fragment annulé après cette
étape reste dans l'historique, suivi de la phrase complète.

## Flow frontend voix (App.tsx)

1. Mount → `useEffect([isInputVisible])` → `startPassiveListeningRef` → `startVAD()` (getUserMedia).
   ⚠️ L'`AudioContext` peut démarrer **suspended** jusqu'au 1er geste utilisateur → le VAD ne
   capte rien tant qu'on n'a pas tap/cliqué une fois.
2. `useVoiceWS` ouvre la connexion `/ws/voice` au mount (une seule fois, callbacks via ref).
3. Fin de prise : `onAudioChunk(pcm16)` la RETIENT dans `pendingAudioRef`, rien ne part encore.
4. `processVoiceTurn()` décide de son sort, puis envoie la prise collée à son `end_audio`.
5. Backend : STT → `PrepareStreamAsync` → `StreamLLMAsync` → TTS phrase par phrase.
6. Callbacks WS : `onTranscript` (affiche « Compris : … », reset + thinking) → `onLLMChunk`
   (`appendChunk`) → `onLLMDone` → `onOrionSpeaking(true/false)` pilote l'état entité.
7. Barge-in : si `isSpeaking && !isPlayingRef && (isTurnActive || phrase reprise)` → `interrupt()`.

## Points de vigilance connus

- AudioContext suspended au 1er chargement (cf. point 1) — un tap initial débloque.
- `npm run build` (tsc strict) doit passer — pas de variables non utilisées.
