import { apiClient } from './api';
import { ENDPOINTS } from '../config/endpoints';
import type { ApiResponse, HealthCheckDto } from '../types/dto';

/**
 * Health Service - Matches HealthController
 */
export const healthService = {
  async getHealth(): Promise<ApiResponse<HealthCheckDto>> {
    const response = await apiClient.get<ApiResponse<HealthCheckDto>>(ENDPOINTS.health);
    return response.data;
  },
};

export default healthService;
