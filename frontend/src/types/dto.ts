// TypeScript interfaces matching Orion.Core.DTOs
// These must match the C# DTOs exactly

// Enums
export enum LLMProvider {
  Ollama = 0,
  Anthropic = 1,
}

// ApiResponse<T> - Matches Orion.Core.DTOs.Responses.ApiResponse<T>
export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  message?: string;
  statusCode: number;
  errors?: Record<string, string[]>;
}

// ChatRequest - Matches Orion.Core.DTOs.Requests.ChatRequest
export interface ChatRequest {
  message: string;
  sessionId?: string; // GUID as string
}

// ChatResponse - Matches Orion.Core.DTOs.Responses.ChatResponse
export interface ChatResponse {
  response: string;
  sessionId: string; // GUID as string
  llmProvider: LLMProvider;
  memoryUsed: boolean;
  toolsCalled?: ToolCallDto[];
}

// ToolCallDto - Matches Orion.Core.DTOs.Responses.ToolCallDto
export interface ToolCallDto {
  toolName: string;
  input: string; // JSON string
  result?: string; // JSON string
}

// DaemonActionRequest - Matches Orion.Core.DTOs.Requests.DaemonActionRequest
export interface DaemonActionRequest {
  action: string;
  payload: unknown;
  requestId: string; // GUID as string
}

// DaemonActionResponse - Matches Orion.Core.DTOs.Responses.DaemonActionResponse
export interface DaemonActionResponse {
  requestId: string;
  success: boolean;
  data?: unknown;
  error?: string;
  timestamp: number; // long in C#
}

// DaemonStatus - Response from GET /api/daemon/status
export interface DaemonStatus {
  connected: boolean;
  machineName: string;
}

// DaemonToolInfo - Response from GET /api/daemon/tools
export interface DaemonToolInfo {
  name: string;
  description: string;
  parameters: string[];
}

// HealthCheckDto - Matches Orion.Core.DTOs.HealthCheckDto
export interface HealthCheckDto {
  llmOnline: boolean;
  daemonConnected: boolean;
  activeProvider: LLMProvider | null;
  timestamp: number;
}

// MemoryVectorDto - Matches Orion.Core.DTOs.MemoryVectorDto
export interface MemoryVectorDto {
  id: string; // GUID as string
  content: string;
  similarity: number; // float
  source?: string;
  createdAt: string; // DateTime as ISO string
}

// BriefingDto - Matches frontend expectations for briefing display
export interface BriefingDto {
  id: string; // GUID as string
  content: string;
  createdAt: string; // DateTime as ISO string
  stats?: Record<string, unknown>;
  // Extended fields for UI display
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
  summary?: string;
}

// VoiceRequest
export interface VoiceRequest {
  audioBase64: string;
  mimeType: string;
}

// VoiceResponse
export interface VoiceResponse {
  transcript: string;
  confidence: number;
}

// ChatMessage - Frontend type for chat history
export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  toolCalls?: ToolCallDto[];
  timestamp: Date;
}

// OrionState - Entity states
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
