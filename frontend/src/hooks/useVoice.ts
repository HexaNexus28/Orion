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
  onTextFallback?: (text: string) => void;
  onError?: (error: string) => void;
}

export const useVoice = (options: UseVoiceOptions = {}) => {
  const { onAudioData, onTranscript, onOrionSpeaking, onTextFallback, onError } = options

  const [state, setState] = useState<VoiceState>({
    isRecording: false,
    isConverting: false,
    error: null,
  })

  const streamRef = useRef<MediaStream | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)
  const analyserRef = useRef<AnalyserNode | null>(null)
  const scriptProcessorRef = useRef<ScriptProcessorNode | null>(null)
  const gainRef = useRef<GainNode | null>(null)
  const pcmChunksRef = useRef<Float32Array[]>([])
  const wavBlobRef = useRef<Blob | null>(null)
  const rafRef = useRef<number | null>(null)

  // Lecteur audio séquentiel
  const audioQueueRef = useRef<ArrayBuffer[]>([])
  const isPlayingRef = useRef(false)
  const playbackCtxRef = useRef<AudioContext | null>(null)
  const nextStartTimeRef = useRef(0)

  // ── Lecteur audio chunk par chunk sans gap ──────────────────────────────────

  const playNextChunk = useCallback(async () => {
    if (isPlayingRef.current || audioQueueRef.current.length === 0) return
    isPlayingRef.current = true

    while (audioQueueRef.current.length > 0) {
      const chunk = audioQueueRef.current.shift()!
      try {
        if (!playbackCtxRef.current || playbackCtxRef.current.state === 'closed') {
          playbackCtxRef.current = new AudioContext({ sampleRate: 24000 })
          nextStartTimeRef.current = playbackCtxRef.current.currentTime
        }
        const ctx = playbackCtxRef.current
        console.log('[useVoice] Decoding WAV chunk:', chunk.byteLength, 'bytes')
        const buffer = await ctx.decodeAudioData(chunk.slice(0))
        const source = ctx.createBufferSource()
        source.buffer = buffer
        source.connect(ctx.destination)
        const startAt = Math.max(ctx.currentTime, nextStartTimeRef.current)
        source.start(startAt)
        nextStartTimeRef.current = startAt + buffer.duration
        console.log('[useVoice] Playing chunk:', buffer.duration.toFixed(2), 's')
      } catch (e) {
        console.error('[useVoice] decodeAudioData failed:', e, 'chunk size:', chunk.byteLength)
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
    sessionId?: string,
    language: string = 'fr'
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
        sessionId,
        language,
        (text) => {
          console.log('[useVoice] Text fallback:', text)
          onTextFallback?.(text)
        }
      )
    } catch (err) {
      const error = err instanceof Error ? err.message : 'Converse failed'
      console.error('[useVoice] Error:', error)
      setState(prev => ({ ...prev, error }))
      onError?.(error)
    } finally {
      setState(prev => ({ ...prev, isConverting: false }))
      // Attend la fin de lecture avant de signaler (max 10s safety)
      let elapsed = 0
      const waitForPlayback = setInterval(() => {
        elapsed += 100
        if ((!isPlayingRef.current && audioQueueRef.current.length === 0) || elapsed > 10000) {
          clearInterval(waitForPlayback)
          onOrionSpeaking?.(false)
        }
      }, 100)
    }
  }, [enqueueAudioChunk, stopPlayback, onTranscript, onOrionSpeaking, onTextFallback, onError])

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

      const source = audioContext.createMediaStreamSource(stream)
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
