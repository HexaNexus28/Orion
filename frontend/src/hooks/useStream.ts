import { useState, useCallback } from 'react';
import { chatService } from '../services/chatService';

interface StreamState {
  text: string;
  isStreaming: boolean;
  error: string | null;
}

export const useStream = () => {
  const [state, setState] = useState<StreamState>({
    text: '',
    isStreaming: false,
    error: null
  });

  const streamMessage = useCallback(async (message: string, sessionId?: string) => {
    setState({ text: '', isStreaming: true, error: null });
    console.log('[useStream] Starting stream for:', message);

    try {
      const stream = chatService.streamMessage({
        message,
        sessionId
      });

      let fullText = '';
      let chunkCount = 0;
      for await (const chunk of stream) {
        chunkCount++;
        fullText += chunk;
        setState(prev => ({ ...prev, text: fullText }));
        if (chunkCount % 10 === 0) {
          console.log('[useStream] Received', chunkCount, 'chunks, text length:', fullText.length);
        }
      }

      console.log('[useStream] Stream complete. Final text length:', fullText.length);
      console.log('[useStream] Final text:', fullText.substring(0, 100) + '...');

      setState(prev => ({ ...prev, isStreaming: false }));
      return fullText;
    } catch (err) {
      const error = err instanceof Error ? err.message : 'Stream error';
      setState(prev => ({ ...prev, isStreaming: false, error }));
      throw err;
    }
  }, []);

  const reset = useCallback(() => {
    setState({ text: '', isStreaming: false, error: null });
  }, []);

  return {
    text: state.text,
    isStreaming: state.isStreaming,
    error: state.error,
    streamMessage,
    reset
  };
};
