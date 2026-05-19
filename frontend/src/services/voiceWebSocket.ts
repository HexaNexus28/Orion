/**
 * VoiceWebSocket — Full-duplex voice client for ORION.
 *
 * Protocol (matches backend VoiceWebSocketHandler):
 *   Client → Server:
 *     - Binary: raw PCM 16kHz mono int16 audio chunks (from VAD)
 *     - JSON: {"type":"end_audio"}   — finalize utterance
 *     - JSON: {"type":"interrupt"}   — cancel current turn
 *     - JSON: {"type":"config","language":"fr","sessionId":"..."}
 *
 *   Server → Client:
 *     - JSON: {"type":"ready"}
 *     - JSON: {"type":"session","id":"..."}
 *     - JSON: {"type":"transcript","text":"..."}
 *     - JSON: {"type":"llm_start"}
 *     - JSON: {"type":"llm_chunk","text":"..."}
 *     - JSON: {"type":"llm_done","text":"..."}
 *     - Binary: WAV audio chunks (complete files)
 *     - JSON: {"type":"tts_done"}
 *     - JSON: {"type":"error","message":"..."}
 *     - JSON: {"type":"interrupted"}
 */

import { API_BASE, ENDPOINTS } from '../config/endpoints';

const WS_URL = API_BASE.replace(/^http/, 'ws') + ENDPOINTS.voiceWS;

export interface VoiceWSCallbacks {
  onReady?: () => void;
  onTranscript?: (text: string) => void;
  onLLMStart?: () => void;
  onLLMChunk?: (text: string) => void;
  onLLMDone?: (fullText: string) => void;
  onAudioChunk?: (wav: ArrayBuffer) => void;
  onTTSDone?: () => void;
  onSession?: (id: string) => void;
  onInterrupted?: () => void;
  onError?: (message: string) => void;
  onDisconnect?: () => void;
}

export class VoiceWebSocket {
  private ws: WebSocket | null = null;
  private callbacks: VoiceWSCallbacks;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private _isConnected = false;
  private sessionId: string | null = null;
  private language = 'fr';

  constructor(callbacks: VoiceWSCallbacks) {
    this.callbacks = callbacks;
  }

  get isConnected(): boolean {
    return this._isConnected;
  }

  connect(): void {
    if (this.ws?.readyState === WebSocket.OPEN) return;

    try {
      this.ws = new WebSocket(WS_URL);
      this.ws.binaryType = 'arraybuffer';

      this.ws.onopen = () => {
        console.log('[VoiceWS] Connected');
        this._isConnected = true;

        // Send config
        this.sendJson({
          type: 'config',
          language: this.language,
          sessionId: this.sessionId,
        });
      };

      this.ws.onmessage = (event) => {
        if (event.data instanceof ArrayBuffer) {
          // Binary → WAV audio chunk
          this.callbacks.onAudioChunk?.(event.data);
        } else {
          // Text → JSON control message
          this.handleJsonMessage(event.data as string);
        }
      };

      this.ws.onclose = () => {
        console.log('[VoiceWS] Disconnected');
        this._isConnected = false;
        this.callbacks.onDisconnect?.();
        this.scheduleReconnect();
      };

      this.ws.onerror = () => {
        if (this._isConnected) {
          console.warn('[VoiceWS] Connection error');
        }
        this._isConnected = false;
      };
    } catch (err) {
      console.error('[VoiceWS] Failed to connect:', err);
    }
  }

  disconnect(): void {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
    if (this.ws) {
      this.ws.onclose = null; // Prevent reconnect
      this.ws.close();
      this.ws = null;
    }
    this._isConnected = false;
  }

  /**
   * Send raw PCM audio data (int16, 16kHz, mono) to the server.
   * Call this continuously during speech detection.
   */
  sendAudio(pcmData: Int16Array): void {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;
    this.ws.send(pcmData.buffer);
  }

  /**
   * Signal end of speech — triggers STT + LLM + TTS pipeline on server.
   */
  endAudio(): void {
    this.sendJson({ type: 'end_audio' });
  }

  /**
   * Interrupt the current turn (barge-in). Cancels LLM + TTS.
   */
  interrupt(): void {
    this.sendJson({ type: 'interrupt' });
  }

  /**
   * Update session config (language, sessionId).
   */
  setConfig(language?: string, sessionId?: string): void {
    if (language) this.language = language;
    if (sessionId) this.sessionId = sessionId;
    if (this._isConnected) {
      this.sendJson({
        type: 'config',
        language: this.language,
        sessionId: this.sessionId,
      });
    }
  }

  // ── Private ─────────────────────────────────────────────────────────────────

  private handleJsonMessage(raw: string): void {
    try {
      const msg = JSON.parse(raw) as Record<string, string>;

      switch (msg.type) {
        case 'ready':
          console.log('[VoiceWS] Server ready');
          this.callbacks.onReady?.();
          break;

        case 'transcript':
          console.log('[VoiceWS] Transcript:', msg.text);
          this.callbacks.onTranscript?.(msg.text);
          break;

        case 'llm_start':
          this.callbacks.onLLMStart?.();
          break;

        case 'llm_chunk':
          this.callbacks.onLLMChunk?.(msg.text);
          break;

        case 'llm_done':
          this.callbacks.onLLMDone?.(msg.text);
          break;

        case 'tts_done':
          this.callbacks.onTTSDone?.();
          break;

        case 'session':
          console.log('[VoiceWS] Session:', msg.id);
          this.sessionId = msg.id;
          this.callbacks.onSession?.(msg.id);
          break;

        case 'interrupted':
          console.log('[VoiceWS] Turn interrupted');
          this.callbacks.onInterrupted?.();
          break;

        case 'error':
          console.error('[VoiceWS] Server error:', msg.message);
          this.callbacks.onError?.(msg.message);
          break;

        default:
          console.warn('[VoiceWS] Unknown message type:', msg.type);
      }
    } catch (err) {
      console.error('[VoiceWS] Invalid JSON:', raw, err);
    }
  }

  private sendJson(data: Record<string, unknown>): void {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;
    this.ws.send(JSON.stringify(data));
  }

  private scheduleReconnect(): void {
    if (this.reconnectTimer) return;
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      console.log('[VoiceWS] Reconnecting...');
      this.connect();
    }, 3000);
  }
}
