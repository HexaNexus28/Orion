# ORION — Frontend Fixes (Kimi/Windsurf)

## Contexte
Stack : React 19 + Vite + TypeScript. Objectif : pipeline voix en streaming
bout-en-bout pour réduire la latence de 4-8s à ~800ms-1.5s.

Le backend aura un nouvel endpoint `POST /api/voice/converse` qui :
- Reçoit l'audio WAV en multipart
- Retourne les bytes WAV en streaming chunk par chunk
- Expose le transcript dans le header `X-Transcript`

---

## Fichier 1 — `frontend/src/config/endpoints.ts`

Ajouter `converse` dans la section voice :

```typescript
voice: {
  transcribe: '/api/voice/transcribe',
  synthesize: '/api/voice/synthesize',
  status: '/api/voice/status',
  converse: '/api/voice/converse',   // AJOUTER
},
```

---

## Fichier 2 — `frontend/src/services/voiceApi.ts`

Ajouter la fonction `converseStream` à la fin du fichier
(après `transcribeBlob`, sans rien supprimer) :

```typescript
/**
 * Pipeline voix complet en streaming bout-en-bout.
 * WAV blob → POST /api/voice/converse → audio WAV stream chunk par chunk.
 *
 * @param audioBlob     Blob WAV produit par useVoice (ScriptProcessorNode)
 * @param onTranscript  Appelé dès que le transcript est dispo (header X-Transcript)
 * @param onAudioChunk  Appelé pour chaque chunk WAV reçu — jouer immédiatement
 * @param sessionId     Session ID optionnel pour continuer une conversation
 */
export const converseStream = async (
  audioBlob: Blob,
  onTranscript: (text: string) => void,
  onAudioChunk: (chunk: ArrayBuffer) => void,
  sessionId?: string
): Promise<void> => {
  const formData = new FormData()
  formData.append('audioFile', audioBlob, 'voice.wav')

  const url = sessionId
    ? `${API_BASE}${ENDPOINTS.voice.converse}?sessionId=${sessionId}`
    : `${API_BASE}${ENDPOINTS.voice.converse}`

  const response = await fetch(url, {
    method: 'POST',
    body: formData,
  })

  if (!response.ok) {
    throw new Error(`Converse failed: ${response.status}`)
  }

  const transcript = response.headers.get('X-Transcript')
  if (transcript) {
    onTranscript(decodeURIComponent(transcript))
  }

  if (!response.body) return

  const reader = response.body.getReader()
  try {
    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      if (value && value.byteLength > 0) {
        onAudioChunk(value.buffer)
      }
    }
  } finally {
    reader.releaseLock()
  }
}
```

---

## Fichier 3 — `frontend/src/hooks/useVoice.ts`

Remplacer le fichier entier par ce contenu.
Changements par rapport à l'existant :
- Ajout de `converseWithOrion` — pipeline streaming principal
- Ajout lecteur audio séquentiel sans gap (audioQueue)
- Ajout `isConverting` dans l'état
- Tout le reste (startRecording, stopRecording, reset) est conservé identique

```typescript
import { useState, useCallback, useRef, useEffect } from 'react';
import { encodeWav, converseStream } from '../services/voiceApi';

interface VoiceState {
  isRecording: boolean;
  isConverting: boolean;
  error: string | null;
}

interface UseVoiceOptions {
  onAudioData?: (data: Float32Array) => void;
  onTranscript?: (text: string) => void;
  onOrionSpeaking?: (speaking: boolean) => void;
  onError?: (error: string) => void;
}

export const useVoice = (options: UseVoiceOptions = {}) => {
  const { onAudioData, onTranscript, onOrionSpeaking, onError } = options

  const [state, setState] = useState<VoiceState>({
    isRecording: false,
    isConverting: false,
    error: null,
  })

  const streamRef          = useRef<MediaStream | null>(null)
  const audioContextRef    = useRef<AudioContext | null>(null)
  const analyserRef        = useRef<AnalyserNode | null>(null)
  const scriptProcessorRef = useRef<ScriptProcessorNode | null>(null)
  const gainRef            = useRef<GainNode | null>(null)
  const pcmChunksRef       = useRef<Float32Array[]>([])
  const wavBlobRef         = useRef<Blob | null>(null)
  const rafRef             = useRef<number | null>(null)

  // Lecteur audio séquentiel
  const audioQueueRef    = useRef<ArrayBuffer[]>([])
  const isPlayingRef     = useRef(false)
  const playbackCtxRef   = useRef<AudioContext | null>(null)
  const nextStartTimeRef = useRef(0)

  // ── Lecteur audio chunk par chunk sans gap ──────────────────────────────────

  const playNextChunk = useCallback(async () => {
    if (isPlayingRef.current || audioQueueRef.current.length === 0) return
    isPlayingRef.current = true

    while (audioQueueRef.current.length > 0) {
      const chunk = audioQueueRef.current.shift()!
      try {
        if (!playbackCtxRef.current || playbackCtxRef.current.state === 'closed') {
          playbackCtxRef.current = new AudioContext()
          nextStartTimeRef.current = playbackCtxRef.current.currentTime
        }
        const ctx = playbackCtxRef.current
        const buffer = await ctx.decodeAudioData(chunk)
        const source = ctx.createBufferSource()
        source.buffer = buffer
        source.connect(ctx.destination)
        const startAt = Math.max(ctx.currentTime, nextStartTimeRef.current)
        source.start(startAt)
        nextStartTimeRef.current = startAt + buffer.duration
      } catch {
        // Chunk partiel — ignore et continue
      }
    }
    isPlayingRef.current = false
  }, [])

  const enqueueAudioChunk = useCallback((chunk: ArrayBuffer) => {
    audioQueueRef.current.push(chunk)
    void playNextChunk()
  }, [playNextChunk])

  const stopPlayback = useCallback(() => {
    audioQueueRef.current = []
    isPlayingRef.current = false
    if (playbackCtxRef.current && playbackCtxRef.current.state !== 'closed') {
      void playbackCtxRef.current.close()
      playbackCtxRef.current = null
    }
    nextStartTimeRef.current = 0
  }, [])

  // ── Pipeline voix streaming ─────────────────────────────────────────────────

  const converseWithOrion = useCallback(async (
    audioBlob: Blob,
    sessionId?: string
  ): Promise<void> => {
    setState(prev => ({ ...prev, isConverting: true, error: null }))
    stopPlayback()
    onOrionSpeaking?.(true)

    try {
      await converseStream(
        audioBlob,
        (transcript) => {
          console.log('[useVoice] Transcript:', transcript)
          onTranscript?.(transcript)
        },
        (chunk) => {
          enqueueAudioChunk(chunk)
        },
        sessionId
      )
    } catch (err) {
      const error = err instanceof Error ? err.message : 'Converse failed'
      console.error('[useVoice] Error:', error)
      setState(prev => ({ ...prev, error }))
      onError?.(error)
    } finally {
      setState(prev => ({ ...prev, isConverting: false }))
      // Attend la fin de lecture avant de signaler
      const waitForPlayback = setInterval(() => {
        if (!isPlayingRef.current && audioQueueRef.current.length === 0) {
          clearInterval(waitForPlayback)
          onOrionSpeaking?.(false)
        }
      }, 100)
    }
  }, [enqueueAudioChunk, stopPlayback, onTranscript, onOrionSpeaking, onError])

  // ── Helpers ─────────────────────────────────────────────────────────────────

  const teardownAudio = useCallback(() => {
    if (rafRef.current) { cancelAnimationFrame(rafRef.current); rafRef.current = null }
    if (scriptProcessorRef.current) {
      try { scriptProcessorRef.current.disconnect() } catch { /* ok */ }
      scriptProcessorRef.current = null
    }
    if (gainRef.current) {
      try { gainRef.current.disconnect() } catch { /* ok */ }
      gainRef.current = null
    }
    if (audioContextRef.current && audioContextRef.current.state !== 'closed') {
      void audioContextRef.current.close()
      audioContextRef.current = null
    }
    analyserRef.current = null
    if (streamRef.current) {
      streamRef.current.getTracks().forEach(t => t.stop())
      streamRef.current = null
    }
  }, [])

  // ── startRecording (identique à l'existant) ─────────────────────────────────

  const startRecording = useCallback(async () => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: { sampleRate: 16000, channelCount: 1, echoCancellation: true, noiseSuppression: true },
      })
      streamRef.current = stream
      pcmChunksRef.current = []
      wavBlobRef.current = null

      const AudioContextClass =
        window.AudioContext ||
        (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext
      const audioContext = new AudioContextClass({ sampleRate: 16000 })
      audioContextRef.current = audioContext

      const source   = audioContext.createMediaStreamSource(stream)
      const analyser = audioContext.createAnalyser()
      analyser.fftSize = 2048
      analyser.smoothingTimeConstant = 0.8
      analyserRef.current = analyser

      const scriptNode = audioContext.createScriptProcessor(4096, 1, 1)
      scriptProcessorRef.current = scriptNode
      scriptNode.onaudioprocess = (e: AudioProcessingEvent) => {
        pcmChunksRef.current.push(new Float32Array(e.inputBuffer.getChannelData(0)))
      }

      const muteGain = audioContext.createGain()
      muteGain.gain.value = 0
      gainRef.current = muteGain

      source.connect(analyser)
      source.connect(scriptNode)
      scriptNode.connect(muteGain)
      muteGain.connect(audioContext.destination)

      const dataArray = new Float32Array(analyser.fftSize)
      const monitor = () => {
        if (!analyserRef.current) return
        analyserRef.current.getFloatTimeDomainData(dataArray)
        onAudioData?.(dataArray)
        rafRef.current = requestAnimationFrame(monitor)
      }
      monitor()

      setState({ isRecording: true, isConverting: false, error: null })
    } catch (err) {
      const error = err instanceof Error ? err.message : 'Microphone access denied'
      setState({ isRecording: false, isConverting: false, error })
      onError?.(error)
      throw err
    }
  }, [onAudioData, onError])

  // ── stopRecording (identique à l'existant) ──────────────────────────────────

  const stopRecording = useCallback(async (): Promise<Blob | null> => {
    if (rafRef.current) { cancelAnimationFrame(rafRef.current); rafRef.current = null }
    if (scriptProcessorRef.current) {
      try { scriptProcessorRef.current.disconnect() } catch { /* ok */ }
      scriptProcessorRef.current = null
    }
    if (gainRef.current) {
      try { gainRef.current.disconnect() } catch { /* ok */ }
      gainRef.current = null
    }
    if (audioContextRef.current && audioContextRef.current.state !== 'closed') {
      await audioContextRef.current.close()
      audioContextRef.current = null
    }
    analyserRef.current = null
    streamRef.current?.getTracks().forEach(t => t.stop())
    streamRef.current = null

    setState(prev => ({ ...prev, isRecording: false }))

    const chunks = pcmChunksRef.current
    if (chunks.length === 0) return null

    const totalLength = chunks.reduce((sum, c) => sum + c.length, 0)
    const combined = new Float32Array(totalLength)
    let offset = 0
    for (const chunk of chunks) { combined.set(chunk, offset); offset += chunk.length }

    const wavBlob = encodeWav(combined, 16000)
    wavBlobRef.current = wavBlob
    pcmChunksRef.current = []
    return wavBlob
  }, [])

  // ── reset ────────────────────────────────────────────────────────────────────

  const reset = useCallback(() => {
    teardownAudio()
    stopPlayback()
    pcmChunksRef.current = []
    wavBlobRef.current = null
    setState({ isRecording: false, isConverting: false, error: null })
  }, [teardownAudio, stopPlayback])

  useEffect(() => () => { teardownAudio(); stopPlayback() }, [teardownAudio, stopPlayback])

  return {
    isRecording: state.isRecording,
    isConverting: state.isConverting,
    error: state.error,
    startRecording,
    stopRecording,
    converseWithOrion,
    stopPlayback,
    reset,
  }
}
```

---

## Fichier 4 — `frontend/src/hooks/useChat.ts`

Trois modifications ciblées — garder tout le reste identique :

**4a — Modifier l'import voiceApi en haut du fichier :**
```typescript
// AVANT
import { transcribeBlob } from '../services/voiceApi';

// APRÈS
import { transcribeBlob, converseStream } from '../services/voiceApi';
```

**4b — Ajouter `onAudioChunk` dans l'interface `UseChatOptions` :**
```typescript
interface UseChatOptions {
  onStateChange?: (state: OrionState) => void;
  onAudioChunk?: (chunk: ArrayBuffer) => void;  // AJOUTER
}
```

**4c — Remplacer la méthode `sendVoiceMessage` :**
```typescript
// AVANT
const sendVoiceMessage = useCallback(async (audioBlob: Blob) => {
  options.onStateChange?.('listening');
  try {
    const transcribeRes = await transcribeBlob(audioBlob);
    if (!transcribeRes.success || !transcribeRes.data) {
      throw new Error(transcribeRes.message || 'Transcription failed');
    }
    const transcript = transcribeRes.data.transcript;
    if (!transcript.trim()) throw new Error('No speech detected');
    await sendMessage(transcript);
  } catch (err) {
    const errorMsg = err instanceof Error ? err.message : 'Voice processing failed';
    setError(errorMsg);
    options.onStateChange?.('error');
  }
}, [sendMessage, options]);

// APRÈS
const sendVoiceMessage = useCallback(async (audioBlob: Blob, sessionId?: string) => {
  options.onStateChange?.('listening');
  setIsStreaming(true);
  setError(null);

  try {
    await converseStream(
      audioBlob,
      (transcript) => {
        const userMsg: ChatMessage = {
          id: Date.now().toString(),
          role: 'user',
          content: transcript,
          timestamp: new Date()
        };
        setMessages(prev => [...prev, userMsg]);
        options.onStateChange?.('thinking');
      },
      (chunk) => {
        options.onAudioChunk?.(chunk);
      },
      sessionId
    );
    options.onStateChange?.('idle');
  } catch (err) {
    const errorMsg = err instanceof Error ? err.message : 'Voice processing failed';
    setError(errorMsg);
    options.onStateChange?.('error');
  } finally {
    setIsStreaming(false);
  }
}, [options]);
```

---

## Fichiers non modifiés

```
useVAD.ts         aucun changement — VAD existant est correct
useStream.ts      aucun changement
useOrionEntity.ts aucun changement
useOrionNotifications.ts  aucun changement
chatService.ts    aucun changement
daemonService.ts  aucun changement
memoryService.ts  aucun changement
```

---

## Comment brancher dans App.tsx / OrionEntity

```typescript
const voice = useVoice({
  onTranscript: (text) => {
    setLastTranscript(text)         // afficher dans l'UI
    entityState.setState('thinking')
  },
  onOrionSpeaking: (speaking) => {
    entityState.setState(speaking ? 'responding' : 'idle')
  },
  onAudioData: (data) => {
    // amplitude micro → animation entité pendant l'écoute
  }
})

const vad = useVAD({
  onSpeechStart: () => entityState.setState('listening'),
  onAudioReady: async (blob) => {
    // UN SEUL APPEL remplace le pipeline 3 étapes
    await voice.converseWithOrion(blob, currentSessionId)
  }
})
```

---

## Résumé

```
endpoints.ts    +1 ligne  (voice.converse)
voiceApi.ts     +1 fonction ajoutée en fin de fichier (converseStream)
useVoice.ts     Fichier remplacé (+converseWithOrion +lecteur audio)
useChat.ts      3 modifications ciblées (import + interface + sendVoiceMessage)
```

Ces changements frontend dépendent du backend — voir ORION_FIXES.md pour
les changements backend correspondants (POST /api/voice/converse).