// chatDto.ts - DTOs Chat
export enum LLMProvider {
  Ollama = 0,
  Anthropic = 1,
}

export interface ChatRequest {
  message: string;
  sessionId?: string;
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

