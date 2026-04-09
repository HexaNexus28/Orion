import { useState, useCallback } from 'react';
import { apiClient } from './api';
import { ENDPOINTS } from '../config/endpoints';
import type { ApiResponse, BriefingDto } from '../types/dto';

// Briefing Service - Future BriefingController
class BriefingService {
  async getToday(): Promise<ApiResponse<BriefingDto>> {
    try {
      const response = await apiClient.get<ApiResponse<BriefingDto>>(ENDPOINTS.briefing.today);
      return response.data;
    } catch {
      return {
        success: false,
        message: 'Briefing not available',
        statusCode: 404,
        data: undefined,
      };
    }
  }

  async getHistory(days: number = 7): Promise<ApiResponse<BriefingDto[]>> {
    try {
      const response = await apiClient.get<ApiResponse<BriefingDto[]>>(
        `${ENDPOINTS.briefing.history}?days=${days}`
      );
      return response.data;
    } catch {
      return {
        success: false,
        message: 'Briefing history not available',
        statusCode: 404,
        data: undefined,
      };
    }
  }
}

export const briefingService = new BriefingService();

export const useBriefing = () => {
  const [briefing, setBriefing] = useState<BriefingDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchToday = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await briefingService.getToday();
      if (response.success && response.data) {
        setBriefing(response.data);
      } else {
        setError(response.message || 'Failed to load briefing');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchHistory = useCallback(async (days: number = 7) => {
    setLoading(true);
    try {
      const response = await briefingService.getHistory(days);
      if (response.success && response.data) {
        // Return array of briefings
        return response.data;
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
    return [];
  }, []);

  return { briefing, loading, error, fetchToday, fetchHistory };
};
