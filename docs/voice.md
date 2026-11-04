# Pipeline vocal

La conversation vocale passe par le WebSocket `/ws/voice`.

```
Frontend                                Backend
────────                                ───────
VAD Silero (fin de prise)
  PCM 16 kHz + end_audio   ───────────► TranscriptionCascade
                           ◄─────────── transcript
                           ◄─────────── llm_start, llm_chunk, tool_start, tool_result
AudioContext 24 kHz        ◄─────────── WAV par phrase (SpeechCascade)
                           ◄─────────── llm_done, tts_done
interrupt                  ───────────► annulation du tour
```

## Protocole

Client vers serveur :

| Message | Rôle |
|---|---|
| binaire | PCM Int16, 16 kHz, mono |
| `config` | langue et session |
| `end_audio` | déclenche le tour sur l'audio reçu |
| `interrupt` | annule le tour en cours |
| `diag` | télémétrie du micro : état, confiance maximale, nombre de prises |

Serveur vers client :

| Message | Rôle |
|---|---|
| `ready`, `session` | connexion établie, identifiant de conversation |
| `transcript`, `no_speech` | texte reconnu, ou aucune parole |
| `llm_start`, `llm_chunk`, `llm_done` | réponse en streaming |
| `tool_start`, `tool_result` | activité des outils |
| binaire | fichier WAV complet, une phrase |
| `tts_done` | fin de l'audio du tour |
| `interrupted`, `error` | tour annulé, ou erreur |
| `superseded` | un autre client a pris la main |

Le protocole transporte du PCM sans en-tête. `MicVAD` rééchantillonne à 16 kHz et
`VoiceWebSocketHandler.EncodePcmToWav` suppose cette fréquence.

## Composants

| Fonction | Composant |
|---|---|
| Détection de parole | `MicVAD`, Silero v5 (`useVAD.ts`) |
| Transcription | `TranscriptionCascade` : Voxtral, puis Whisper.net |
| Synthèse | `SpeechCascade` : Voxtral TTS, puis Kokoro via le daemon |
| Lecture | `useVoiceWS.ts`, `AudioContext` |

Réglages de `useVAD.ts` :

| Paramètre | Valeur |
|---|---|
| Seuil de parole | 0,5 |
| Seuil de fin de parole | 0,35 |
| Silence de clôture | environ 450 ms |
| Parole minimale | environ 290 ms |
| Pré-roll | environ 256 ms |

La synthèse Voxtral renvoie un WAV 24 kHz mono 16 bits, comme Kokoro. Elle prend environ 0,4 s pour
une phrase courte et 1,2 s pour 130 caractères. Le texte des réponses vocales est donc envoyé à
Mistral.

## Déroulement d'un tour

1. Le VAD clôt une prise. `onAudioChunk` la retient dans `pendingAudioRef`.
2. `processVoiceTurn` décide de son sort, puis envoie l'audio avec `end_audio`.
3. Le backend transcrit, prépare le contexte et lance `StreamLLMAsync`.
4. Le texte est découpé en phrases. Chaque phrase est synthétisée et envoyée dans l'ordre.
5. Le frontend affiche « Compris : … » et joue l'audio à la suite.

Découpage du texte : fin de phrase à partir de 20 caractères, ponctuation faible à partir de 80,
coupure forcée à 150.

## Écho

- Une prise n'est émise que par `processVoiceTurn`, avec le `end_audio` qui la consomme.
- `isPlayingRef` reste vrai jusqu'au `onended` du dernier son.
- Une prise commencée pendant un tour est écartée, sauf si une interruption a été déclarée.
- ORION ne peut pas être interrompu pendant qu'il parle : ce que le micro capte est traité comme de
  l'écho. Il peut l'être pendant qu'il réfléchit.

Le mode texte ne déclenche aucune synthèse vocale.

## Phrases interrompues

La prise se clôt après environ 450 ms de silence. Si la parole reprend dans les 3 s suivant l'envoi
de la première prise (`PHRASE_RESUME_WINDOW_MS`), et si ORION n'a ni lancé d'outil ni fini de
répondre, le tour est annulé et les prises sont renvoyées ensemble.

Si la reprise n'était qu'un bruit bref (`onMisfire`), la prise d'origine est renvoyée telle quelle.

Le message utilisateur est écrit dès `PrepareStreamAsync` : un fragment annulé après cette étape
reste dans l'historique.

## Un seul client

`VoiceConnectionRegistry` n'autorise qu'une connexion vocale active. La dernière connexion remplace
la précédente, qui reçoit `superseded`, coupe son micro et propose de reprendre.

## Contraintes navigateur

- La capture audio exige une interaction utilisateur. Le micro s'arme au premier contact avec
  l'interface.
- `/vad/silero_vad_v5.onnx` doit être servi avec un type MIME reconnu.
