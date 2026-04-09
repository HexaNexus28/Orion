import { useState, useCallback } from 'react';
import { apiClient } from './api';
import { ENDPOINTS } from '../config/endpoints';
import type { ApiResponse, MemoryVectorDto } from '../types/dto';

// Memory Service - Future MemoryController
class MemoryService {
  async search(query: string, limit: number = 5): Promise<ApiResponse<MemoryVectorDto[]>> {
    try {
      const response = await apiClient.post<ApiResponse<MemoryVectorDto[]>>(
        ENDPOINTS.memory.search,
        { query, limit }
      );
      return response.data;
    } catch {
      // Fallback if endpoint not available
      return {
        success: false,
        message: 'Memory search not available',
        statusCode: 404,
        data: undefined,
      };
    }
  }

  async getAll(): Promise<ApiResponse<MemoryVectorDto[]>> {
    try {
      const response = await apiClient.get<ApiResponse<MemoryVectorDto[]>>(ENDPOINTS.memory.list);
      return response.data;
    } catch {
      return {
        success: false,
        message: 'Memory list not available',
        statusCode: 404,
        data: undefined,
      };
    }
  }

  async delete(id: string): Promise<ApiResponse<void>> {
    try {
      const response = await apiClient.delete<ApiResponse<void>>(ENDPOINTS.memory.delete(id));
      return response.data;
    } catch {
      return {
        success: false,
        message: 'Memory delete not available',
        statusCode: 404,
        data: undefined,
      };
    }
  }
}

export const memoryService = new MemoryService();

// Hook for memory operations
export const useMemory = () => {
  const [memories, setMemories] = useState<MemoryVectorDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const search = useCallback(async (query: string) => {
    setLoading(true);
    setError(null);
    try {
      const response = await memoryService.search(query);
      if (response.success && response.data) {
        setMemories(response.data);
      } else {
        setError(response.message || 'Search failed');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  }, []);

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      const response = await memoryService.getAll();
      if (response.success && response.data) {
        setMemories(response.data);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  }, []);

  return { memories, loading, error, search, refresh };
};
