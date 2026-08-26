import { useState, useCallback, useRef, useEffect } from 'react';
import { VoiceWebSocket } from '../services/voiceWebSocket';

interface UseVoiceWSOptions {
  onTranscript?: (text: string) => void;
  onLLMChunk?: (text: string) => void;
  onNoSpeech?: () => void;
  onToolStart?: (tool: string, args?: string) => void;
  onToolResult?: (tool: string, ok: boolean, summary?: string) => void;
  onLLMDone?: (fullText: string) => void;
  onOrionSpeaking?: (speaking: boolean) => void;
  onAmplitude?: (amplitude: number) => void;
  onError?: (error: string) => void;
}

/**
 * useVoiceWS — Full-duplex voice hook using WebSocket.
 *
 * Replaces the old HTTP POST pipeline with a persistent WebSocket connection.
 * Supports: streaming audio, barge-in, multi-turn sessions, low latency.
 */
export const useVoiceWS = (options: UseVoiceWSOptions = {}) => {
  const [isConnected, setIsConnected] = useState(false);
  const [isTurnActive, setIsTurnActive] = useState(false);
  const wsRef = useRef<VoiceWebSocket | null>(null);

  // Stable refs for callbacks — avoids recreating connect/disconnect on every render
  const cbRef = useRef(options);
  cbRef.current = options;

  // Audio playback
  const audioQueueRef = useRef<ArrayBuffer[]>([]);
  const isPlayingRef = useRef(false);
  const playbackCtxRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const amplitudeRafRef = useRef<number | null>(null);
  const nextStartTimeRef = useRef(0);

  // ── Amplitude polling — feeds orb shader during TTS playback ─────────────
  const startAmplitudeLoop = useCallback((ctx: AudioContext, analyser: AnalyserNode) => {
    const buf = new Uint8Array(analyser.frequencyBinCount);
    const tick = () => {
      analyser.getByteTimeDomainData(buf);
      let sum = 0;
      for (let i = 0; i < buf.length; i++) {
        const v = (buf[i] - 128) / 128;
        sum += v * v;
      }
      const rms = Math.sqrt(sum / buf.length);
      cbRef.current.onAmplitude?.(rms);
      if (ctx.state !== 'closed') {
        amplitudeRafRef.current = requestAnimationFrame(tick);
      }
    };
    amplitudeRafRef.current = requestAnimationFrame(tick);
  }, []);

  const stopAmplitudeLoop = useCallback(() => {
    if (amplitudeRafRef.current !== null) {
      cancelAnimationFrame(amplitudeRafRef.current);
      amplitudeRafRef.current = null;
    }
    cbRef.current.onAmplitude?.(0);
  }, []);

  // ── Audio playback — pipelined, gapless ────────────────────────────────────
  // Pre-decode the next chunk while the current one plays to eliminate gaps

  const playNextChunk = useCallback(async () => {
    if (isPlayingRef.current || audioQueueRef.current.length === 0) return;
    isPlayingRef.current = true;

    // Ensure AudioContext + AnalyserNode are alive
    if (!playbackCtxRef.current || playbackCtxRef.current.state === 'closed') {
      const ctx = new AudioContext({ sampleRate: 24000 });
      const analyser = ctx.createAnalyser();
      analyser.fftSize = 256;
      analyser.connect(ctx.destination);
      playbackCtxRef.current = ctx;
      analyserRef.current = analyser;
      nextStartTimeRef.current = ctx.currentTime;
      startAmplitudeLoop(ctx, analyser);
    }
    const ctx = playbackCtxRef.current;
    const analyser = analyserRef.current!;

    // Pre-decode first chunk
    let nextDecoded: AudioBuffer | null = null;
    if (audioQueueRef.current.length > 0) {
      try {
        nextDecoded = await ctx.decodeAudioData(audioQueueRef.current.shift()!.slice(0));
      } catch (e) {
        console.error('[useVoiceWS] decodeAudioData failed:', e);
      }
    }

    while (nextDecoded) {
      const buffer = nextDecoded;

      // Start decoding the NEXT chunk in parallel with playback
      const decodePromise = audioQueueRef.current.length > 0
        ? ctx.decodeAudioData(audioQueueRef.current.shift()!.slice(0)).catch(() => null)
        : Promise.resolve(null);

      // Schedule current buffer for gapless playback — route through analyser
      const source = ctx.createBufferSource();
      source.buffer = buffer;
      source.connect(analyser);
      const startAt = Math.max(ctx.currentTime, nextStartTimeRef.current);
      source.start(startAt);
      nextStartTimeRef.current = startAt + buffer.duration;

      // Await next decode (runs during playback of current)
      nextDecoded = await decodePromise;
    }

    isPlayingRef.current = false;
  }, [startAmplitudeLoop]);

  const stopPlayback = useCallback(() => {
    audioQueueRef.current = [];
    isPlayingRef.current = false;
    stopAmplitudeLoop();
    if (playbackCtxRef.current && playbackCtxRef.current.state !== 'closed') {
      void playbackCtxRef.current.close();
      playbackCtxRef.current = null;
      analyserRef.current = null;
    }
    nextStartTimeRef.current = 0;
  }, [stopAmplitudeLoop]);

  // ── WebSocket lifecycle (stable — no callback deps) ───────────────────────

  // Auto-connect on mount, disconnect on unmount — runs exactly ONCE
  useEffect(() => {
    const ws = new VoiceWebSocket({
      onReady: () => {
        setIsConnected(true);
      },

      onTranscript: (text) => {
        cbRef.current.onTranscript?.(text);
      },

      onLLMStart: () => {
        setIsTurnActive(true);
        cbRef.current.onOrionSpeaking?.(true);
      },

      onNoSpeech: () => {
        cbRef.current.onNoSpeech?.();
      },

      onToolStart: (tool, args) => {
        cbRef.current.onToolStart?.(tool, args);
      },

      onToolResult: (tool, ok, summary) => {
        cbRef.current.onToolResult?.(tool, ok, summary);
      },

      onLLMChunk: (text) => {
        cbRef.current.onLLMChunk?.(text);
      },

      onLLMDone: (fullText) => {
        cbRef.current.onLLMDone?.(fullText);
      },

      onAudioChunk: (wav) => {
        console.log('[useVoiceWS] Audio chunk received:', (wav.byteLength / 1024).toFixed(1), 'KB');
        audioQueueRef.current.push(wav);
        void playNextChunk();
      },

      onTTSDone: () => {
        const checkDone = setInterval(() => {
          if (!isPlayingRef.current && audioQueueRef.current.length === 0) {
            clearInterval(checkDone);
            stopAmplitudeLoop();
            setIsTurnActive(false);
            cbRef.current.onOrionSpeaking?.(false);
          }
        }, 50);
        setTimeout(() => {
          clearInterval(checkDone);
          stopAmplitudeLoop();
          setIsTurnActive(false);
          cbRef.current.onOrionSpeaking?.(false);
        }, 15000);
      },

      onInterrupted: () => {
        stopPlayback();
        setIsTurnActive(false);
        cbRef.current.onOrionSpeaking?.(false);
      },

      onError: (message) => {
        cbRef.current.onError?.(message);
        setIsTurnActive(false);
        cbRef.current.onOrionSpeaking?.(false);
      },

      onDisconnect: () => {
        setIsConnected(false);
      },
    });

    ws.connect();
    wsRef.current = ws;

    return () => {
      ws.disconnect();
      wsRef.current = null;
      setIsConnected(false);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // Intentionally empty — connect once, callbacks via ref

  // ── Voice actions ─────────────────────────────────────────────────────────

  const sendAudio = useCallback((pcm16: Int16Array) => {
    wsRef.current?.sendAudio(pcm16);
  }, []);

  const endAudio = useCallback(() => {
    wsRef.current?.endAudio();
  }, []);

  const interrupt = useCallback(() => {
    stopPlayback();
    wsRef.current?.interrupt();
    setIsTurnActive(false);
    cbRef.current.onOrionSpeaking?.(false);
  }, [stopPlayback]);

  /** Telemetrie du micro — voir VoiceWebSocket.sendDiagnostic. */
  const sendDiagnostic = useCallback((ctx: string, maxAmp: number, chunks: number) => {
    wsRef.current?.sendDiagnostic(ctx, maxAmp, chunks);
  }, []);

  return {
    isConnected,
    isTurnActive,
    sendDiagnostic,
    sendAudio,
    endAudio,
    interrupt,
    stopPlayback,
  };
};
