import { useState, useCallback } from 'react';
import { chatService } from '../services/chatService';
import { transcribeBlob } from '../services/voiceApi';
import type { OrionState } from '../types';

interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  timestamp: Date;
}

interface UseChatOptions {
  onStateChange?: (state: OrionState) => void;
}

export const useChat = (options: UseChatOptions = {}) => {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const sendMessage = useCallback(async (content: string) => {
    const userMsg: ChatMessage = {
      id: Date.now().toString(),
      role: 'user',
      content,
      timestamp: new Date()
    };

    setMessages(prev => [...prev, userMsg]);
    setIsStreaming(true);
    setError(null);
    options.onStateChange?.('thinking');

    try {
      const response = await chatService.sendMessage({
        message: content
      });

      if (response.success && response.data) {
        const assistantMsg: ChatMessage = {
          id: (Date.now() + 1).toString(),
          role: 'assistant',
          content: response.data.response,
          timestamp: new Date()
        };
        setMessages(prev => [...prev, assistantMsg]);
        options.onStateChange?.('idle');
      } else {
        setError(response.message || 'Failed to get response');
        options.onStateChange?.('error');
      }
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Network error';
      setError(errorMsg);
      options.onStateChange?.('error');
    } finally {
      setIsStreaming(false);
    }
  }, [options]);

  const sendVoiceMessage = useCallback(async (audioBlob: Blob) => {
    options.onStateChange?.('listening');

    try {
      // 1. Transcribe
      const transcribeRes = await transcribeBlob(audioBlob);
      if (!transcribeRes.success || !transcribeRes.data) {
        throw new Error(transcribeRes.message || 'Transcription failed');
      }

      const transcript = transcribeRes.data.transcript;
      if (!transcript.trim()) {
        throw new Error('No speech detected');
      }

      // 2. Send as text message
      await sendMessage(transcript);
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Voice processing failed';
      setError(errorMsg);
      options.onStateChange?.('error');
    }
  }, [sendMessage, options]);

  const clear = useCallback(() => {
    setMessages([]);
    setError(null);
  }, []);

  return {
    messages,
    isStreaming,
    error,
    sendMessage,
    sendVoiceMessage,
    clear
  };
};
