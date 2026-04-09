// toolDto.ts - DTOs Tools
export interface ToolCallDto {
  toolName: string;
  input: string;
  result?: string;
}

export interface ToolInfo {
  name: string;
  description: string;
  parameters: string[];
}

// Memory Tools DTOs
export interface MemorySaveRequest {
  content: string;
  source?: string;
  importance?: number;
}

export interface MemoryUpdateRequest {
  id: string;
  content: string;
}

export interface MemoryForgetRequest {
  id: string;
}

export interface ProfileUpdateRequest {
  key: string;
  value: string;
}
