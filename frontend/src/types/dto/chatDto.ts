// chatDto.ts - DTOs Chat

/**
 * Miroir de `Orion.Core.Enums.LLMProvider`.
 *
 * ASP.NET sérialise cet enum en CHAÎNE ("Ollama"), pas en nombre — vérifié sur /api/health.
 * L'ancienne déclaration `enum { Ollama = 0, Anthropic = 1 }` était fausse deux fois :
 * mauvais type (numérique vs chaîne) ET mauvaises valeurs (le backend a `None, Ollama`,
 * donc None=0 et Ollama=1). Toute comparaison `x === LLMProvider.Ollama` était
 * systématiquement fausse.
 */
export type LLMProvider = 'None' | 'Ollama' | 'Nim';

export interface ChatRequest {
  message: string;
  sessionId?: string;
  /** Active le prompt voix côté backend (phrases courtes, zéro markdown). */
  voiceMode?: boolean;
}

export interface ChatResponse {
  response: string;
  sessionId: string;
  llmProvider: LLMProvider;
  memoryUsed: boolean;
  toolsCalled?: ToolCallDto[];
}

export interface ToolCallDto {
  toolName: string;
  input: string;
  result?: string;
}

// ChatMessage - Frontend type for chat history
export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  toolCalls?: ToolCallDto[];
  timestamp: Date;
}

