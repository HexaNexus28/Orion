import { useState, useCallback } from 'react';
import { chatService } from '../services/chatService';
import type { ToolActivity } from '../types/dto/agentDto';

interface StreamState {
  text: string;
  isStreaming: boolean;
  error: string | null;
  /** Ce qu'ORION a FAIT pendant le tour — pas seulement ce qu'il a dit. */
  tools: ToolActivity[];
}

const EMPTY: StreamState = { text: '', isStreaming: false, error: null, tools: [] };

export const useStream = () => {
  const [state, setState] = useState<StreamState>(EMPTY);

  const streamMessage = useCallback(async (message: string, sessionId?: string) => {
    setState({ ...EMPTY, isStreaming: true });

    try {
      const stream = chatService.streamMessage({ message, sessionId });

      let fullText = '';

      for await (const event of stream) {
        switch (event.type) {
          case 'token':
            fullText += event.text ?? '';
            setState(prev => ({ ...prev, text: fullText }));
            break;

          case 'tool_start':
            setState(prev => ({
              ...prev,
              tools: [
                ...prev.tools,
                {
                  tool: event.tool ?? '?',
                  args: event.args,
                  status: 'running',
                  iteration: event.iteration,
                },
              ],
            }));
            break;

          case 'tool_result':
            setState(prev => {
              // Clôture le dernier appel encore en cours pour cet outil.
              const tools = [...prev.tools];
              for (let i = tools.length - 1; i >= 0; i--) {
                if (tools[i].tool === event.tool && tools[i].status === 'running') {
                  tools[i] = {
                    ...tools[i],
                    status: event.ok ? 'ok' : 'failed',
                    summary: event.summary,
                  };
                  break;
                }
              }
              return { ...prev, tools };
            });
            break;

          case 'error':
            setState(prev => ({ ...prev, error: event.text ?? 'Erreur agent' }));
            break;

          case 'done':
            break;
        }
      }

      setState(prev => ({ ...prev, isStreaming: false }));
      return fullText;
    } catch (err) {
      const error = err instanceof Error ? err.message : 'Stream error';
      setState(prev => ({ ...prev, isStreaming: false, error }));
      throw err;
    }
  }, []);

  const reset = useCallback(() => setState(EMPTY), []);

  // Utilisé par VoiceWS pour injecter les tokens LLM dans l'affichage
  const appendChunk = useCallback((chunk: string) => {
    setState(prev => ({ ...prev, text: prev.text + chunk, isStreaming: true }));
  }, []);

  // Utilisé par VoiceWS pour refléter les actions d'ORION pendant un tour vocal
  const pushTool = useCallback((activity: ToolActivity) => {
    setState(prev => {
      if (activity.status === 'running') {
        return { ...prev, tools: [...prev.tools, activity] };
      }

      const tools = [...prev.tools];
      for (let i = tools.length - 1; i >= 0; i--) {
        if (tools[i].tool === activity.tool && tools[i].status === 'running') {
          tools[i] = { ...tools[i], status: activity.status, summary: activity.summary };
          return { ...prev, tools };
        }
      }
      return { ...prev, tools: [...tools, activity] };
    });
  }, []);

  const setStreaming = useCallback((streaming: boolean) => {
    setState(prev => ({ ...prev, isStreaming: streaming }));
  }, []);

  return {
    text: state.text,
    isStreaming: state.isStreaming,
    error: state.error,
    tools: state.tools,
    streamMessage,
    reset,
    appendChunk,
    pushTool,
    setStreaming,
  };
};
