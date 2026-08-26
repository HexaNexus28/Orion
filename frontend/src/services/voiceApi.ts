// Voice API Service - Uses apiClient and endpoints.ts (Pattern ORION)
import { assertSessionValide, fetchAuthHeaders } from './authService';
import { API_BASE, ENDPOINTS } from '../config/endpoints';
import { apiClient } from './api';
import type { ApiResponse } from '../types';

interface WebKitWindow extends Window {
  webkitAudioContext?: typeof AudioContext;
}

export interface VoiceTranscribeRequest {
  audioBase64: string;
  mimeType: string;
  language?: string;
}

export interface VoiceTranscribeResponse {
  transcript: string;
  confidence: number;
  language: string;
}

export interface VoiceStatusResponse {
  isReady: boolean;
  supportedLanguages: string[];
}

/**
 * Transcribe audio to text using Whisper STT
 * POST /api/voice/transcribe/json
 */
export const transcribeAudio = async (
  request: VoiceTranscribeRequest
): Promise<ApiResponse<VoiceTranscribeResponse>> => {
  const response = await apiClient.post<ApiResponse<VoiceTranscribeResponse>>(
    ENDPOINTS.voice.transcribe + '/json',
    request
  );
  return response.data;
};

/**
 * Get Whisper service status
 * GET /api/voice/status
 */
export const getVoiceStatus = async (): Promise<ApiResponse<VoiceStatusResponse>> => {
  const response = await apiClient.get<ApiResponse<VoiceStatusResponse>>(
    ENDPOINTS.voice.status
  );
  return response.data;
};

/**
 * Convert Blob to base64 string
 */
export const blobToBase64 = (blob: Blob): Promise<string> => {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onloadend = () => {
      const base64 = reader.result?.toString().split(',')[1];
      if (base64) resolve(base64);
      else reject(new Error('Failed to convert to base64'));
    };
    reader.onerror = reject;
    reader.readAsDataURL(blob);
  });
};

const resampleChannelData = (input: Float32Array, sourceRate: number, targetRate: number): Float32Array => {
  if (sourceRate === targetRate) {
    return input;
  }

  const sampleRatio = sourceRate / targetRate;
  const outputLength = Math.max(1, Math.round(input.length / sampleRatio));
  const output = new Float32Array(outputLength);

  for (let i = 0; i < outputLength; i++) {
    const sourceIndex = i * sampleRatio;
    const indexLower = Math.floor(sourceIndex);
    const indexUpper = Math.min(indexLower + 1, input.length - 1);
    const interpolation = sourceIndex - indexLower;
    output[i] = input[indexLower] * (1 - interpolation) + input[indexUpper] * interpolation;
  }

  return output;
};

export const encodeWav = (samples: Float32Array, sampleRate: number): Blob => {
  const bytesPerSample = 2;
  const blockAlign = bytesPerSample;
  const buffer = new ArrayBuffer(44 + samples.length * bytesPerSample);
  const view = new DataView(buffer);

  const writeString = (offset: number, value: string) => {
    for (let i = 0; i < value.length; i++) {
      view.setUint8(offset + i, value.charCodeAt(i));
    }
  };

  writeString(0, 'RIFF');
  view.setUint32(4, 36 + samples.length * bytesPerSample, true);
  writeString(8, 'WAVE');
  writeString(12, 'fmt ');
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true);
  view.setUint16(22, 1, true);
  view.setUint32(24, sampleRate, true);
  view.setUint32(28, sampleRate * blockAlign, true);
  view.setUint16(32, blockAlign, true);
  view.setUint16(34, 16, true);
  writeString(36, 'data');
  view.setUint32(40, samples.length * bytesPerSample, true);

  let offset = 44;
  for (let i = 0; i < samples.length; i++) {
    const sample = Math.max(-1, Math.min(1, samples[i]));
    view.setInt16(offset, sample < 0 ? sample * 0x8000 : sample * 0x7fff, true);
    offset += bytesPerSample;
  }

  return new Blob([buffer], { type: 'audio/wav' });
};

// Supprimé — non utilisé depuis le passage à ScriptProcessorNode dans useVoice
// La fonction ci-dessous était utilisée pour convertir WebM→WAV via AudioContext
// keepée pour éviter git diff trop large — peut être supprimée proprement
export const convertBlobToWav = async (blob: Blob): Promise<Blob> => {
  const arrayBuffer = await blob.arrayBuffer();
  const AudioContextClass = window.AudioContext || (window as unknown as WebKitWindow).webkitAudioContext;
  const audioContext = new AudioContextClass();

  try {
    const decodedAudio = await audioContext.decodeAudioData(arrayBuffer.slice(0));
    const primaryChannel = decodedAudio.getChannelData(0);
    const monoSamples = decodedAudio.numberOfChannels === 1
      ? primaryChannel
      : primaryChannel.map((_, index) => {
        let mixedSample = 0;
        for (let channel = 0; channel < decodedAudio.numberOfChannels; channel++) {
          mixedSample += decodedAudio.getChannelData(channel)[index] ?? 0;
        }
        return mixedSample / decodedAudio.numberOfChannels;
      });

    const samples = monoSamples instanceof Float32Array
      ? monoSamples
      : Float32Array.from(monoSamples);
    const resampled = resampleChannelData(samples, decodedAudio.sampleRate, 16000);
    return encodeWav(resampled, 16000);
  } finally {
    // Removed audioContext.close() to avoid TypeScript issues
  }
};

/**
 * Synthèse vocale via Kokoro sur le daemon.
 * Retourne un ArrayBuffer WAV à jouer via AudioContext.
 * Retourne null si le daemon est déconnecté ou Kokoro indisponible (→ fallback Web Speech).
 * POST /api/voice/synthesize
 */
export const synthesizeText = async (text: string): Promise<ArrayBuffer | null> => {
  try {
    const response = await apiClient.post(
      ENDPOINTS.voice.synthesize,
      { text },
      { responseType: 'arraybuffer', timeout: 20000 }
    );
    return response.data as ArrayBuffer;
  } catch (err: unknown) {
    // 503 = daemon indisponible → signal fallback
    if (err && typeof err === 'object' && 'response' in err) {
      const axiosErr = err as { response?: { status?: number } };
      if (axiosErr.response?.status === 503) return null;
    }
    throw err;
  }
};

/**
 * Transcrit un Blob WAV directement (useVoice produit déjà du WAV via ScriptProcessorNode)
 */
export const transcribeBlob = async (
  blob: Blob,
  language?: string
): Promise<ApiResponse<VoiceTranscribeResponse>> => {
  const base64 = await blobToBase64(blob);
  return transcribeAudio({
    audioBase64: base64,
    mimeType: 'audio/wav',
    language,
  });
};

/**
 * Pipeline voix complet en streaming bout-en-bout.
 * WAV blob → POST /api/voice/converse → audio WAV stream chunk par chunk.
 *
 * @param audioBlob     Blob WAV produit par useVoice (ScriptProcessorNode)
 * @param onTranscript  Appelé dès que le transcript est dispo (header X-Transcript)
 * @param onAudioChunk  Appelé pour chaque chunk WAV reçu — jouer immédiatement
 * @param sessionId     Session ID optionnel pour continuer une conversation
 * @param language      Langue de transcription (défaut: 'fr' pour français)
 */
export const converseStream = async (
  audioBlob: Blob,
  onTranscript: (text: string) => void,
  onAudioChunk: (chunk: ArrayBuffer) => void,
  sessionId?: string,
  language: string = 'fr',
  onTextFallback?: (text: string) => void
): Promise<void> => {
  const formData = new FormData();
  formData.append('audioFile', audioBlob, 'voice.wav');

  const params = new URLSearchParams();
  if (sessionId) params.append('sessionId', sessionId);
  if (language) params.append('language', language);

  const url = `${API_BASE}${ENDPOINTS.voice.converse}${params.toString() ? '?' + params.toString() : ''}`;

  // fetch() is required for streaming binary response (axios does not support ReadableStream).
  // Propagate auth headers from apiClient to maintain consistent auth behavior.
  const headers = fetchAuthHeaders();

  const response = await fetch(url, {
    method: 'POST',
    headers,
    body: formData,
  });

  assertSessionValide(response);

  if (!response.ok) {
    throw new Error(`Converse failed: ${response.status}`);
  }

  const transcript = response.headers.get('X-Transcript');
  if (transcript) {
    onTranscript(decodeURIComponent(transcript));
  }

  if (!response.body) return;

  // Détecte le fallback texte (Kokoro indisponible → backend envoie du texte)
  const contentType = response.headers.get('Content-Type') || '';
  const isTextFallback = contentType.includes('text/plain');

  const reader = response.body.getReader();
  try {
    if (isTextFallback) {
      // Fallback texte — accumuler et décoder
      const textChunks: Uint8Array[] = [];
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        if (value && value.byteLength > 0) textChunks.push(value);
      }
      if (textChunks.length > 0) {
        const decoder = new TextDecoder('utf-8');
        const fullText = textChunks.map(c => decoder.decode(c, { stream: true })).join('') + decoder.decode();
        console.log('[converseStream] Text fallback:', fullText);
        onTextFallback?.(fullText);
      }
    } else {
      // Audio WAV — accumuler un buffer et extraire les fichiers WAV complets
      let pending = new Uint8Array(0);
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        if (!value || value.byteLength === 0) continue;

        // Concaténer au buffer pending
        const merged = new Uint8Array(pending.length + value.length);
        merged.set(pending);
        merged.set(value, pending.length);
        pending = merged;

        // Extraire les WAV complets (chaque WAV: RIFF + taille dans bytes 4-7)
        while (pending.length >= 8) {
          const view = new DataView(pending.buffer, pending.byteOffset, pending.byteLength);
          const riff = pending[0] === 0x52 && pending[1] === 0x49 && pending[2] === 0x46 && pending[3] === 0x46; // 'RIFF'
          if (!riff) {
            console.warn('[converseStream] Non-RIFF data, skipping', pending.length, 'bytes');
            pending = new Uint8Array(0);
            break;
          }
          const wavSize = view.getUint32(4, true) + 8; // RIFF header size = 8
          if (pending.length < wavSize) break; // Pas encore assez de données

          // Extraire un WAV complet
          const wavData = pending.slice(0, wavSize);
          pending = pending.slice(wavSize);
          onAudioChunk(wavData.buffer);
        }
      }

      // Flush remaining data si c'est un WAV complet
      if (pending.length >= 8) {
        const riff = pending[0] === 0x52 && pending[1] === 0x49 && pending[2] === 0x46 && pending[3] === 0x46;
        if (riff) onAudioChunk(pending.buffer);
      }
    }
  } finally {
    reader.releaseLock();
  }
};
