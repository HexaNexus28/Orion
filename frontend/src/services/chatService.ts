import { apiClient } from './api';
import { API_BASE, ENDPOINTS } from '../config/endpoints';
import type { ApiResponse } from '../types/api/apiResponse';
import type { ChatRequest, ChatResponse } from '../types/dto/chatDto';

// Chat Service - Matches ChatController (axios pattern)
export const chatService = {
  async sendMessage(request: ChatRequest): Promise<ApiResponse<ChatResponse>> {
    const response = await apiClient.post<ApiResponse<ChatResponse>>(
      ENDPOINTS.chat.send,
      request
    );
    return response.data;
  },

  async *streamMessage(request: ChatRequest): AsyncGenerator<string> {
    const response = await fetch(`${API_BASE}${ENDPOINTS.chat.stream}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });

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
          const cleaned = line.replace(/\r$/, '');
          if (!cleaned.startsWith('data: ')) continue;
          const data = cleaned.slice(6);
          if (data === '[DONE]') return;
          if (data) yield data;
        }
      }

      // Traiter les données résiduelles dans le buffer
      const cleaned = buffer.replace(/\r$/, '');
      if (cleaned.startsWith('data: ')) {
        const data = cleaned.slice(6);
        if (data && data !== '[DONE]') yield data;
      }
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
