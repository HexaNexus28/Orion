import { useRef, useCallback, useState, useEffect } from 'react';
import { encodeWav } from '../services/voiceApi';

interface UseVADOptions {
  onSpeechStart?: () => void;
  onSpeechEnd?: (audio: Float32Array) => void;
  onAudioReady?: (blob: Blob) => void;
  onAudioChunk?: (pcm16: Int16Array) => void; // Streaming: called during speech with PCM int16 chunks
  onAmplitude?: (amplitude: number) => void;
  onError?: (error: string) => void;
}

/**
 * useVAD — Voice Activity Detection par amplitude (Web Audio API)
 *
 * Pas de dépendances ONNX/WASM. Détection fiable via RMS.
 * Pipeline :
 *   getUserMedia → ScriptProcessorNode → RMS > seuil → speech
 *   Silence SILENCE_TIMEOUT_MS → onSpeechEnd(Float32Array PCM 16kHz)
 */

const SPEECH_THRESHOLD = 0.015;   // RMS amplitude — ajuster si trop/peu sensible
const SILENCE_TIMEOUT_MS = 700;   // Silence avant de clôturer la prise (700ms pour conversation naturelle)
const MIN_SPEECH_MS = 250;        // Durée min pour ne pas déclencher sur un bruit court
const SAMPLE_RATE = 16000;

// 2048 échantillons à 16 kHz = 128 ms par bloc.
// Le RMS est calculé sur le bloc ENTIER : avec des blocs de 256 ms, un mot qui démarre en milieu
// de bloc voit son énergie diluée par le silence qui précède, et le bloc est classé « silence ».
const BUFFER_SIZE = 2048;

// Pré-roll : blocs conservés en permanence AVANT la détection de parole.
// Sans lui, l'attaque du premier mot est perdue — le seuil RMS n'est franchi qu'une fois la
// voyelle installée. « Ouvre Notepad » arrivait en « ...vre Notepad » côté Whisper.
// 4 blocs x 128 ms = 512 ms d'historique, largement de quoi couvrir une attaque de mot.
const PREROLL_CHUNKS = 4;

export const useVAD = (options: UseVADOptions = {}) => {
  const [isSpeaking, setIsSpeaking] = useState(false);
  const [isListening, setIsListening] = useState(false);

  const audioCtxRef = useRef<AudioContext | null>(null);
  const sourceRef = useRef<MediaStreamAudioSourceNode | null>(null);
  const processorRef = useRef<ScriptProcessorNode | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const samplesRef = useRef<Float32Array[]>([]);
  const prerollRef = useRef<Float32Array[]>([]);
  const silenceTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const speechStartTs = useRef<number | null>(null);
  const speakingRef = useRef(false);
  const listeningRef = useRef(false);

  const { onSpeechStart, onSpeechEnd, onAudioReady, onAudioChunk, onAmplitude, onError } = options;

  const clearSilence = useCallback(() => {
    if (silenceTimer.current !== null) {
      clearTimeout(silenceTimer.current);
      silenceTimer.current = null;
    }
  }, []);

  const finalizeSpeech = useCallback(() => {
    if (!speakingRef.current) return;

    const duration = speechStartTs.current ? Date.now() - speechStartTs.current : 0;
    speakingRef.current = false;
    speechStartTs.current = null;
    setIsSpeaking(false);

    if (duration < MIN_SPEECH_MS) {
      samplesRef.current = [];
      console.log('[VAD] Prise trop courte, ignorée');
      return;
    }

    const totalLen = samplesRef.current.reduce((acc, s) => acc + s.length, 0);
    const combined = new Float32Array(totalLen);
    let offset = 0;
    for (const chunk of samplesRef.current) {
      combined.set(chunk, offset);
      offset += chunk.length;
    }
    samplesRef.current = [];

    console.log('[VAD] onSpeechEnd — durée:', duration, 'ms | samples:', combined.length);
    onSpeechEnd?.(combined);

    const wav = encodeWav(combined, SAMPLE_RATE);
    console.log('[VAD] WAV encodé:', wav.size, 'bytes');
    onAudioReady?.(wav);
  }, [onSpeechEnd, onAudioReady]);

  const start = useCallback(async () => {
    if (listeningRef.current) return;

    try {
      console.log('[VAD] Démarrage écoute...');

      // Diagnostic explicite de l'autorisation micro.
      // Sans ça, un micro refusé — ou une invite jamais validée — laisse `getUserMedia` en
      // attente indéfinie : ORION reste silencieusement sourd et l'interface se contente
      // d'afficher « vad inactif », sans jamais dire pourquoi. Observé au test Playwright.
      if (navigator.permissions?.query) {
        try {
          const permission = await navigator.permissions.query({
            name: 'microphone' as PermissionName,
          });
          if (permission.state === 'denied') {
            console.warn('[VAD] Autorisation micro refusée');
            onError?.("Micro refusé — autorise l'accès au microphone dans les réglages du navigateur, puis recharge la page.");
            return;
          }
          if (permission.state === 'prompt') {
            console.log('[VAD] En attente de l’autorisation micro…');
          }
        } catch {
          // Permissions API indisponible (Safari) : on tente getUserMedia directement.
        }
      }

      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          sampleRate: SAMPLE_RATE,
          channelCount: 1,
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
        },
      });

      const AudioCtx = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
      const ctx = new AudioCtx({ sampleRate: SAMPLE_RATE });

      const source = ctx.createMediaStreamSource(stream);
      const processor = ctx.createScriptProcessor(BUFFER_SIZE, 1, 1);
      const mute = ctx.createGain();
      mute.gain.value = 0;

      processor.onaudioprocess = (e) => {
        if (!listeningRef.current) return;

        const data = e.inputBuffer.getChannelData(0);

        // RMS amplitude
        let sum = 0;
        for (let i = 0; i < data.length; i++) sum += data[i] * data[i];
        const rms = Math.sqrt(sum / data.length);

        onAmplitude?.(Math.min(1, rms / 0.1));

        if (rms > SPEECH_THRESHOLD) {
          if (!speakingRef.current) {
            speakingRef.current = true;
            speechStartTs.current = Date.now();
            setIsSpeaking(true);
            onSpeechStart?.();
            console.log('[VAD] Parole détectée — RMS:', rms.toFixed(4));

            // Rejoue le pré-roll : sans lui le début du premier mot est perdu.
            samplesRef.current = [...prerollRef.current];
            if (onAudioChunk) {
              for (const buffered of prerollRef.current) {
                onAudioChunk(floatTo16BitPCM(buffered));
              }
            }
            prerollRef.current = [];
          }
          clearSilence();
          const chunk = new Float32Array(data);
          samplesRef.current.push(chunk);
          // Stream PCM int16 to WebSocket in real-time
          if (onAudioChunk) {
            const int16 = floatTo16BitPCM(chunk);
            onAudioChunk(int16);
          }
        } else if (!speakingRef.current) {
          // Silence : on garde une fenêtre glissante pour le prochain démarrage de parole.
          prerollRef.current.push(new Float32Array(data));
          if (prerollRef.current.length > PREROLL_CHUNKS) prerollRef.current.shift();
        } else if (speakingRef.current) {
          const chunk = new Float32Array(data);
          samplesRef.current.push(chunk);
          if (onAudioChunk) {
            const int16 = floatTo16BitPCM(chunk);
            onAudioChunk(int16);
          }
          if (silenceTimer.current === null) {
            silenceTimer.current = setTimeout(() => {
              clearSilence();
              console.log('[VAD] Silence → fin de prise');
              finalizeSpeech();
            }, SILENCE_TIMEOUT_MS);
          }
        }
      };

      source.connect(processor);
      processor.connect(mute);
      mute.connect(ctx.destination);

      audioCtxRef.current = ctx;
      sourceRef.current = source;
      processorRef.current = processor;
      streamRef.current = stream;
      listeningRef.current = true;
      setIsListening(true);

      console.log('[VAD] Écoute active ✓');
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Accès microphone refusé';
      console.error('[VAD] Erreur démarrage:', err);
      onError?.(msg);
    }
  }, [onSpeechStart, onSpeechEnd, onAudioReady, onAudioChunk, onAmplitude, onError, clearSilence, finalizeSpeech]);

  const pause = useCallback(() => {
    listeningRef.current = false;
    clearSilence();
    if (speakingRef.current) finalizeSpeech();
    setIsListening(false);
    setIsSpeaking(false);
  }, [clearSilence, finalizeSpeech]);

  const destroy = useCallback(() => {
    listeningRef.current = false;
    speakingRef.current = false;
    clearSilence();

    processorRef.current?.disconnect();
    sourceRef.current?.disconnect();
    streamRef.current?.getTracks().forEach(t => t.stop());
    audioCtxRef.current?.close().catch(() => { });

    audioCtxRef.current = null;
    sourceRef.current = null;
    processorRef.current = null;
    streamRef.current = null;
    samplesRef.current = [];
    prerollRef.current = [];
    speechStartTs.current = null;

    setIsListening(false);
    setIsSpeaking(false);
  }, [clearSilence]);

  const reset = useCallback(() => { destroy(); }, [destroy]);

  const resume = useCallback(async () => {
    if (!audioCtxRef.current) {
      await start();
    } else {
      listeningRef.current = true;
      setIsListening(true);
    }
  }, [start]);

  useEffect(() => () => { destroy(); }, [destroy]);

  return { isSpeaking, isListening, start, pause, resume, destroy, reset };
};

/** Convert Float32 PCM [-1,1] to Int16 PCM for WebSocket streaming */
function floatTo16BitPCM(float32: Float32Array): Int16Array {
  const int16 = new Int16Array(float32.length);
  for (let i = 0; i < float32.length; i++) {
    const s = Math.max(-1, Math.min(1, float32[i]));
    int16[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
  }
  return int16;
}
