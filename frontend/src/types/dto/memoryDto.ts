// memoryDto.ts - DTOs Memory
export interface MemoryVectorDto {
  id: string;
  content: string;
  similarity: number;
  source?: string;
  createdAt: string;
}

export interface MemorySearchRequest {
  query: string;
  limit?: number;
}
