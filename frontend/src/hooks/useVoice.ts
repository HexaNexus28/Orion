import { useState, useCallback, useRef, useEffect } from 'react';
import { encodeWav } from '../services/voiceApi';

interface VoiceState {
  isRecording: boolean;
  error: string | null;
}

interface UseVoiceOptions {
  onAudioData?: (data: Float32Array) => void;
  onError?: (error: string) => void;
}

/**
 * useVoice — Capture PCM brut via ScriptProcessorNode
 *
 * Remplace l'approche MediaRecorder + decodeAudioData qui échouait
 * sur certains blobs WebM/Opus incomplets.
 *
 * Pipeline :
 *   getUserMedia → AudioContext(16kHz) → AnalyserNode → VAD amplitude
 *                                      → ScriptProcessorNode → Float32Array[]
 *   stopRecording → encodeWav(pcm, 16000) → Blob WAV prêt pour Whisper
 */
export const useVoice = (options: UseVoiceOptions = {}) => {
  const { onAudioData, onError } = options;

  const [state, setState] = useState<VoiceState>({
    isRecording: false,
    error: null,
  });

  const streamRef            = useRef<MediaStream | null>(null);
  const audioContextRef      = useRef<AudioContext | null>(null);
  const analyserRef          = useRef<AnalyserNode | null>(null);
  const scriptProcessorRef   = useRef<ScriptProcessorNode | null>(null);
  const gainRef              = useRef<GainNode | null>(null);
  const pcmChunksRef         = useRef<Float32Array[]>([]);
  const wavBlobRef           = useRef<Blob | null>(null);
  const rafRef               = useRef<number | null>(null);

  // ── Helpers ─────────────────────────────────────────────────────────────────

  const teardownAudio = useCallback(() => {
    if (rafRef.current) {
      cancelAnimationFrame(rafRef.current);
      rafRef.current = null;
    }

    if (scriptProcessorRef.current) {
      try { scriptProcessorRef.current.disconnect(); } catch { /* ok */ }
      scriptProcessorRef.current = null;
    }

    if (gainRef.current) {
      try { gainRef.current.disconnect(); } catch { /* ok */ }
      gainRef.current = null;
    }

    if (audioContextRef.current && audioContextRef.current.state !== 'closed') {
      void audioContextRef.current.close();
      audioContextRef.current = null;
    }

    analyserRef.current = null;

    if (streamRef.current) {
      streamRef.current.getTracks().forEach(t => t.stop());
      streamRef.current = null;
    }
  }, []);

  // ── startRecording ───────────────────────────────────────────────────────────

  const startRecording = useCallback(async () => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          sampleRate: 16000,
          channelCount: 1,
          echoCancellation: true,
          noiseSuppression: true,
        },
      });

      streamRef.current = stream;
      pcmChunksRef.current = [];
      wavBlobRef.current = null;

      // AudioContext forcé à 16 kHz (requis par Whisper)
      const AudioContextClass =
        window.AudioContext ||
        (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
      const audioContext = new AudioContextClass({ sampleRate: 16000 });
      audioContextRef.current = audioContext;

      const source   = audioContext.createMediaStreamSource(stream);
      const analyser = audioContext.createAnalyser();
      analyser.fftSize = 2048;
      analyser.smoothingTimeConstant = 0.8;
      analyserRef.current = analyser;

      // ScriptProcessorNode — capture PCM brut (non smoothé)
      // Taille 4096 = ~256 ms par chunk à 16 kHz
      const scriptNode = audioContext.createScriptProcessor(4096, 1, 1);
      scriptProcessorRef.current = scriptNode;

      scriptNode.onaudioprocess = (e: AudioProcessingEvent) => {
        const input = e.inputBuffer.getChannelData(0);
        pcmChunksRef.current.push(new Float32Array(input));
      };

      // Gain muet pour satisfaire Chrome (ScriptProcessor doit être connecté à destination)
      const muteGain = audioContext.createGain();
      muteGain.gain.value = 0;
      gainRef.current = muteGain;

      // Graph : source → analyser (VAD)
      //         source → scriptNode → muteGain → destination (PCM)
      source.connect(analyser);
      source.connect(scriptNode);
      scriptNode.connect(muteGain);
      muteGain.connect(audioContext.destination);

      // Boucle amplitude pour le VAD
      const dataArray = new Float32Array(analyser.fftSize);
      const monitor = () => {
        if (!analyserRef.current) return;
        analyserRef.current.getFloatTimeDomainData(dataArray);
        onAudioData?.(dataArray);
        rafRef.current = requestAnimationFrame(monitor);
      };
      monitor();

      setState({ isRecording: true, error: null });
    } catch (err) {
      const error = err instanceof Error ? err.message : 'Microphone access denied';
      setState({ isRecording: false, error });
      onError?.(error);
      throw err;
    }
  }, [onAudioData, onError]);

  // ── stopRecording ────────────────────────────────────────────────────────────

  const stopRecording = useCallback(async (): Promise<Blob | null> => {
    // Stopper l'animation frame et le ScriptProcessor
    if (rafRef.current) {
      cancelAnimationFrame(rafRef.current);
      rafRef.current = null;
    }

    if (scriptProcessorRef.current) {
      try { scriptProcessorRef.current.disconnect(); } catch { /* ok */ }
      scriptProcessorRef.current = null;
    }

    if (gainRef.current) {
      try { gainRef.current.disconnect(); } catch { /* ok */ }
      gainRef.current = null;
    }

    // Fermer l'AudioContext
    if (audioContextRef.current && audioContextRef.current.state !== 'closed') {
      await audioContextRef.current.close();
      audioContextRef.current = null;
    }

    analyserRef.current = null;

    // Arrêter le stream
    streamRef.current?.getTracks().forEach(t => t.stop());
    streamRef.current = null;

    setState(prev => ({ ...prev, isRecording: false }));

    // Encoder les chunks PCM en WAV
    const chunks = pcmChunksRef.current;
    if (chunks.length === 0) return null;

    const totalLength = chunks.reduce((sum, c) => sum + c.length, 0);
    const combined = new Float32Array(totalLength);
    let offset = 0;
    for (const chunk of chunks) {
      combined.set(chunk, offset);
      offset += chunk.length;
    }

    const wavBlob = encodeWav(combined, 16000);
    wavBlobRef.current = wavBlob;
    pcmChunksRef.current = [];

    return wavBlob;
  }, []);

  // ── reset ────────────────────────────────────────────────────────────────────

  const reset = useCallback(() => {
    teardownAudio();
    pcmChunksRef.current = [];
    wavBlobRef.current = null;
    setState({ isRecording: false, error: null });
  }, [teardownAudio]);

  // Cleanup on unmount
  useEffect(() => {
    return () => { teardownAudio(); };
  }, [teardownAudio]);

  return {
    isRecording: state.isRecording,
    error: state.error,
    startRecording,
    stopRecording,
    reset,
  };
};
