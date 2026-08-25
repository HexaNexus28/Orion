// orionStatus.ts - Statut ORION
import type { LLMProvider } from '../dto/chatDto';

export interface OrionStatus {
  llmOnline: boolean;
  daemonConnected: boolean;
  activeProvider: LLMProvider | null;
  lastMessage?: string;
}
