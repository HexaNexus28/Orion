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
import { authService } from './authService';

/**
 * URL construite a CHAQUE connexion, jamais au chargement du module.
 *
 * Le jeton n'existe pas encore quand ce fichier est evalue : au premier chargement, l'ecran de
 * connexion n'a pas ete rempli. Une constante figee au niveau module embarquerait donc un jeton
 * vide pour toute la duree de la session — la connexion partirait sans authentification et
 * serait refusee, sans que rien n'indique pourquoi. C'est exactement le piege qui avait rendu
 * le chat muet sur telephone.
 *
 * Le jeton passe par l'URL parce qu'un WebSocket de navigateur ne peut porter aucun en-tete.
 * Cote serveur, /ws/voice figure dans OrionAuth.QueryTokenPaths — la liste FERMEE des seuls
 * chemins ou ce contournement est accepte.
 */
async function buildWsUrl(): Promise<string> {
  const base = API_BASE.replace(/^http/, 'ws') + ENDPOINTS.voiceWS;
  // BILLET, pas le jeton de session : ce qui part dans une URL doit expirer en une minute.
  const ticket = await authService.getStreamTicket();
  return `${base}?access_token=${encodeURIComponent(ticket)}`;
}

/** Message JSON reçu du backend sur /ws/voice. */
interface VoiceWSMessage {
  type: string;
  text?: string;
  id?: string;
  message?: string;
  tool?: string;
  args?: string;
  ok?: boolean;
  summary?: string;
}

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
  /** Aucune parole reconnue dans la prise (bruit ambiant) — ce n'est PAS une erreur. */
  onNoSpeech?: () => void;
  /** ORION commence a executer un outil pendant le tour vocal. */
  onToolStart?: (tool: string, args?: string) => void;
  /** Resultat de cet outil. */
  onToolResult?: (tool: string, ok: boolean, summary?: string) => void;
}

export class VoiceWebSocket {
  private ws: WebSocket | null = null;
  private callbacks: VoiceWSCallbacks;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private _isConnected = false;
  private sessionId: string | null = null;
  private language = 'fr';
  /** Echecs consecutifs SANS ouverture reussie — sert au palier et au message d'erreur. */
  private echecsConsecutifs = 0;

  constructor(callbacks: VoiceWSCallbacks) {
    this.callbacks = callbacks;
  }

  /**
   * Telemetrie du micro. Le serveur ne peut PAS distinguer « contexte audio en pause » de
   * « parole trop faible » : les deux donnent exactement le meme silence. Et la console d'un
   * telephone est illisible a distance. Le client rapporte donc ce qu il mesure.
   */
  sendDiagnostic(ctx: string, maxAmp: number, chunks: number): void {
    this.sendJson({ type: 'diag', ctx, maxAmp, chunks });
  }

  get isConnected(): boolean {
    return this._isConnected;
  }

  /**
   * Asynchrone parce qu il faut d abord obtenir un billet de flux. L appelant n a pas a
   * l attendre : les rappels (onReady, onError) portent deja le resultat.
   */
  async connect(): Promise<void> {
    if (this.ws?.readyState === WebSocket.OPEN) return;

    try {
      this.ws = new WebSocket(await buildWsUrl());
      this.ws.binaryType = 'arraybuffer';

      this.ws.onopen = () => {
        console.log('[VoiceWS] Connected');
        this._isConnected = true;
        this.echecsConsecutifs = 0;

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
      // Passe ICI notamment quand le billet de flux est refuse (session absente ou expiree).
      // Se contenter de journaliser laisserait un micro definitivement muet, sans rien a
      // l ecran : le navigateur ne signale aucun echec puisque la connexion n a jamais ete
      // tentee. On previent l interface ET on replanifie.
      console.error('[VoiceWS] Connexion impossible :', err);
      this._isConnected = false;
      this.scheduleReconnect();
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
      const msg = JSON.parse(raw) as VoiceWSMessage;

      switch (msg.type) {
        case 'ready':
          console.log('[VoiceWS] Server ready');
          this.callbacks.onReady?.();
          break;

        case 'transcript':
          console.log('[VoiceWS] Transcript:', msg.text);
          this.callbacks.onTranscript?.(msg.text ?? '');
          break;

        case 'llm_start':
          this.callbacks.onLLMStart?.();
          break;

        case 'llm_chunk':
          this.callbacks.onLLMChunk?.(msg.text ?? '');
          break;

        case 'llm_done':
          this.callbacks.onLLMDone?.(msg.text ?? '');
          break;

        case 'no_speech':
          console.log('[VoiceWS] Aucune parole reconnue — prise ignoree');
          this.callbacks.onNoSpeech?.();
          break;

        case 'tool_start':
          console.log('[VoiceWS] Outil demarre:', msg.tool);
          this.callbacks.onToolStart?.(msg.tool ?? '?', msg.args);
          break;

        case 'tool_result':
          console.log('[VoiceWS] Outil termine:', msg.tool, msg.ok);
          this.callbacks.onToolResult?.(msg.tool ?? '?', msg.ok === true, msg.summary);
          break;

        case 'tts_done':
          this.callbacks.onTTSDone?.();
          break;

        case 'session':
          console.log('[VoiceWS] Session:', msg.id);
          this.sessionId = msg.id ?? null;
          this.callbacks.onSession?.(msg.id ?? '');
          break;

        case 'interrupted':
          console.log('[VoiceWS] Turn interrupted');
          this.callbacks.onInterrupted?.();
          break;

        case 'error':
          console.error('[VoiceWS] Server error:', msg.message);
          this.callbacks.onError?.(msg.message ?? 'Erreur serveur');
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

    // Palier exponentiel plafonne a 30 s. Avant : 3 s fixes, indefiniment. Un refus du
    // serveur (jeton invalide) ne se resout JAMAIS tout seul : l'ancien code martelait donc
    // l'API toutes les 3 secondes pour l'eternite, sans qu'aucune de ces tentatives puisse
    // aboutir. Le plafond garde une reconnexion utile apres une vraie coupure reseau.
    this.echecsConsecutifs += 1;
    const delai = Math.min(3000 * 2 ** (this.echecsConsecutifs - 1), 30000);

    // Le navigateur ne donne AUCUN statut HTTP sur un WebSocket refuse (onclose, code 1006).
    // Apres quelques echecs on previent l'interface : sans ca, un micro muet reste
    // inexplicable pour l'utilisateur.
    if (this.echecsConsecutifs === 4) {
      this.callbacks.onError?.('Connexion vocale impossible — verifier la session');
    }

    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      console.log(`[VoiceWS] Reconnexion (tentative ${this.echecsConsecutifs})...`);
      // Chaque reconnexion redemande un billet FRAIS — celui de la tentative precedente a
      // expire depuis longtemps.
      void this.connect();
    }, delai);
  }
}
