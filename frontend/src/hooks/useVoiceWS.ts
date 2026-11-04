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

/** Marge au-delà de la fin prévue de l'audio, si `onended` ne se déclenche jamais. */
const PLAYBACK_SAFETY_MARGIN_MS = 3000;

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
  // Deux questions distinctes. `isSchedulingRef` : une boucle de décodage tourne-t-elle ?
  // `activeSourcesRef` : quels sons sont programmés et pas encore finis ? Programmer un son rend
  // la main aussitôt : seul `onended` dit qu'il s'est tu.
  const isSchedulingRef = useRef(false);
  const activeSourcesRef = useRef(new Set<AudioBufferSourceNode>());
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

  const settlePlayback = useCallback(() => {
    if (
      !isSchedulingRef.current &&
      activeSourcesRef.current.size === 0 &&
      audioQueueRef.current.length === 0
    ) {
      isPlayingRef.current = false;
    }
  }, []);

  const playNextChunk = useCallback(async () => {
    if (isSchedulingRef.current || audioQueueRef.current.length === 0) return;
    isSchedulingRef.current = true;
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

    // `stopPlayback` ferme le contexte pendant un décodage : la boucle ne doit pas lui survivre.
    while (nextDecoded && ctx === playbackCtxRef.current) {
      const buffer = nextDecoded;

      // Start decoding the NEXT chunk in parallel with playback
      const decodePromise = audioQueueRef.current.length > 0
        ? ctx.decodeAudioData(audioQueueRef.current.shift()!.slice(0)).catch(() => null)
        : Promise.resolve(null);

      // Schedule current buffer for gapless playback — route through analyser
      const source = ctx.createBufferSource();
      source.buffer = buffer;
      source.connect(analyser);
      activeSourcesRef.current.add(source);
      source.onended = () => {
        activeSourcesRef.current.delete(source);
        settlePlayback();
      };
      const startAt = Math.max(ctx.currentTime, nextStartTimeRef.current);
      source.start(startAt);
      nextStartTimeRef.current = startAt + buffer.duration;

      // Await next decode (runs during playback of current)
      nextDecoded = await decodePromise;
    }

    if (ctx !== playbackCtxRef.current) return;
    isSchedulingRef.current = false;

    // Un morceau arrivé pendant le dernier décodage a trouvé la boucle occupée : personne d'autre
    // ne le relancera, et la fin de la réponse resterait muette dans la file.
    if (audioQueueRef.current.length > 0) {
      void playNextChunk();
      return;
    }
    settlePlayback();
  }, [startAmplitudeLoop, settlePlayback]);

  const stopPlayback = useCallback(() => {
    audioQueueRef.current = [];
    activeSourcesRef.current.forEach((source) => { source.onended = null; });
    activeSourcesRef.current.clear();
    isSchedulingRef.current = false;
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
        // Le filet de sécurité vit DANS la boucle : un minuteur séparé, laissé armé, clôturait
        // 15 s plus tard le tour SUIVANT, en pleine réponse.
        //
        // Son échéance n'est calculée qu'une fois le décodage fini : `tts_done` arrive pendant
        // celui du dernier morceau, quand `nextStartTimeRef` ne le compte pas encore. Calculée
        // trop tôt, elle coupait toute dernière phrase plus longue que la marge.
        let deadline: number | null = null;
        const finish = () => {
          clearInterval(checkDone);
          isPlayingRef.current = false;
          stopAmplitudeLoop();
          setIsTurnActive(false);
          cbRef.current.onOrionSpeaking?.(false);
        };
        const checkDone = setInterval(() => {
          if (!isPlayingRef.current && audioQueueRef.current.length === 0) {
            finish();
            return;
          }
          if (isSchedulingRef.current) return;

          if (deadline === null) {
            const ctx = playbackCtxRef.current;
            const remainingMs = ctx ? Math.max(0, nextStartTimeRef.current - ctx.currentTime) * 1000 : 0;
            deadline = Date.now() + remainingMs + PLAYBACK_SAFETY_MARGIN_MS;
          }
          if (Date.now() > deadline) finish();
        }, 50);
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

    /**
     * ORION émet-il du son en ce moment ? Une ref, pas un état : la lire ne doit pas provoquer
     * de rendu, et le barge-in la consulte au moment précis où le VAD signale de la parole.
     */
    isPlayingRef,
  };
};
