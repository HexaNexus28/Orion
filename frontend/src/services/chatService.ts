import { apiClient } from './api';
import { assertSessionValide, fetchAuthHeaders } from './authService';
import { API_BASE, ENDPOINTS } from '../config/endpoints';
import type { ApiResponse } from '../types/api/apiResponse';
import type { ChatRequest, ChatResponse } from '../types/dto/chatDto';
import type { AgentEvent } from '../types/dto/agentDto';

const DONE = Symbol('sse-done');

/**
 * Parse une ligne SSE en événement agent.
 * Renvoie null si la ligne ne porte pas d'événement, DONE sur le marqueur de fin.
 */
function parseSseLine(line: string): AgentEvent | typeof DONE | null {
  const cleaned = line.replace(/\r$/, '');
  if (!cleaned.startsWith('data: ')) return null;

  const data = cleaned.slice(6);
  if (!data) return null;
  if (data === '[DONE]') return DONE;

  try {
    return JSON.parse(data) as AgentEvent;
  } catch {
    console.warn('[chatService] Evenement SSE illisible ignore:', data.slice(0, 120));
    return null;
  }
}

// Chat Service - Matches ChatController (axios pattern)
export const chatService = {
  async sendMessage(request: ChatRequest): Promise<ApiResponse<ChatResponse>> {
    const response = await apiClient.post<ApiResponse<ChatResponse>>(
      ENDPOINTS.chat.send,
      request
    );
    return response.data;
  },

  /**
   * Flux d'événements de la boucle agent : tokens, appels d'outils, fin, erreur.
   * Le backend émet un objet JSON par événement — l'ancien format texte brut cassait
   * le cadrage SSE dès qu'un token contenait un retour à la ligne.
   */
  async *streamMessage(request: ChatRequest): AsyncGenerator<AgentEvent> {
    // fetch() est OBLIGATOIRE ici : axios ne sait pas lire un ReadableStream, donc pas de SSE.
    // Le jeton est donc lu a la SOURCE (authService) et non dans apiClient : l intercepteur
    // d apiClient ne s applique qu a ses propres requetes, et `defaults.headers.common` est
    // VIDE puisque le jeton n y est jamais ecrit. C est ce qui faisait partir ce flux sans
    // Authorization — 401 cote serveur, et cote interface un chat muet, sans message d erreur.
    const headers = fetchAuthHeaders({ 'Content-Type': 'application/json' });

    const response = await fetch(`${API_BASE}${ENDPOINTS.chat.stream}`, {
      method: 'POST',
      headers,
      body: JSON.stringify(request),
    });

    // Session invalide : ce flux passe par fetch(), donc HORS de l intercepteur d apiClient.
    // Sans ce bloc, un jeton perime ne declenche ni purge ni ecran de connexion — le chat
    // affiche "Stream failed: 401" indefiniment alors qu il suffirait de se reconnecter.
    // 403 compte AUSSI : un jeton emis avant l ajout du role owner est refuse ainsi, et le
    // front n appelle aucune route reservee au daemon.
    assertSessionValide(response);

    if (!response.ok || !response.body) {
      throw new Error(`Stream failed: ${response.status}`);
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder('utf-8');
    let buffer = '';

    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() ?? '';

        for (const line of lines) {
          const event = parseSseLine(line);
          if (event === DONE) return;
          if (event) yield event;
        }
      }

      // Traiter les données résiduelles dans le buffer
      const trailing = parseSseLine(buffer);
      if (trailing && trailing !== DONE) yield trailing;
    } finally {
      reader.releaseLock();
    }
  },

  async getConversation(id: string): Promise<ApiResponse<ChatResponse[]>> {
    const response = await apiClient.get<ApiResponse<ChatResponse[]>>(
      `${ENDPOINTS.chat.send}/${id}`
    );
    return response.data;
  }
};

export default chatService;
