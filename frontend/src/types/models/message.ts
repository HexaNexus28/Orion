// message.ts - Modèle Message
import { ToolCallDto } from '../dto/toolDto';

export interface Message {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  toolCalls?: ToolCallDto[];
  timestamp: Date;
}

// Alias pour compatibilité
export type ChatMessage = Message;
