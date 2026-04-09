// Legacy API types - re-exported from dto.ts where applicable

export interface ChatRequest {
  message: string;
  conversationId?: string;
  useVoice?: boolean;
}

export interface ToolCall {
  toolName: string;
  input: Record<string, unknown>;
  result?: unknown;
  status: 'pending' | 'executing' | 'completed' | 'error';
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  toolCalls?: ToolCall[];
  timestamp: Date;
}

export interface MemoryVector {
  id: string;
  content: string;
  source?: string;
  importance: number;
  createdAt: string;
}

export interface VoiceResponse {
  transcript: string;
  confidence: number;
}

export interface BriefingDto {
  date: string;
  shiftStarStats?: {
    votes: number;
    rating: number;
    mrr: number;
  };
  calendarEvents?: Array<{
    title: string;
    time: string;
  }>;
  unreadEmails?: number;
  summary: string;
}

export type OrionState = 'idle' | 'listening' | 'thinking' | 'responding' | 'error';

// Component props
export interface OrionEntityProps {
  state: OrionState;
  amplitude?: number;
  onClick?: () => void;
}

export interface ResponseTextProps {
  text: string;
  isStreaming?: boolean;
  speed?: 'slow' | 'normal' | 'fast';
}

export interface UnifiedInputProps {
  onSubmit: (text: string) => void;
  onVoiceStart?: () => void;
  onVoiceEnd?: (transcript: string) => void;
  disabled?: boolean;
  state: OrionState;
}
