// index.ts - Réexportation centrale des types ORION

import type { OrionState } from './models/entityState';
export type { OrionState };

// =============================================================================
// MODELS (export first - used by component props)
// =============================================================================
export type {
  EntityState,
} from './models/entityState';

export type {
  Message,
  ChatMessage as LegacyChatMessage,
} from './models/message';

export type {
  OrionStatus,
} from './models/orionStatus';

// =============================================================================
// API TYPES
// =============================================================================
export type { ApiResponse } from './api/apiResponse';

// =============================================================================
// DTOS
// =============================================================================
export type {
  LLMProvider,
  ChatRequest,
  ChatResponse,
  ChatMessage,
  ToolCallDto,
} from './dto/chatDto';

export type {
  ToolInfo,
  MemorySaveRequest,
  MemoryUpdateRequest,
  MemoryForgetRequest,
  ProfileUpdateRequest,
} from './dto/toolDto';

export type {
  MemoryVectorDto,
  MemorySearchRequest,
} from './dto/memoryDto';

export type {
  BriefingDto,
} from './dto/briefingDto';

export type {
  VoiceRequest,
  VoiceResponse,
} from './dto/voiceDto';

export type {
  DaemonActionRequest,
  DaemonActionResponse,
  DaemonStatus,
  DaemonToolInfo,
} from './dto/daemonDto';

export type {
  HealthCheckDto,
} from './dto/healthDto';

// =============================================================================
// COMPONENT PROPS
// =============================================================================
export interface OrionEntityProps {
  state: OrionState;
  amplitude?: number;
  onTap?: () => void;
  onLongPress?: () => void;
  onLongPressEnd?: () => void;
  onDoubleTap?: () => void;
}

export interface ResponseTextProps {
  text: string;
  isStreaming?: boolean;
  speed?: 'slow' | 'normal' | 'fast';
}

export interface SlideInputProps {
  isVisible: boolean;
  onSubmit: (text: string) => void;
  onVoiceStart?: () => void;
  onVoiceEnd?: (transcript: string) => void;
  disabled?: boolean;
  state: OrionState;
  onClose: () => void;
}

// =============================================================================
// LEGACY TYPES (deprecated - for backward compatibility only)
// =============================================================================
export interface MemoryVector {
  id: string;
  content: string;
  source?: string;
  importance: number;
  createdAt: string;
}

export interface ToolCall {
  toolName: string;
  input: Record<string, unknown>;
  result?: unknown;
  status: 'pending' | 'executing' | 'completed' | 'error';
}
