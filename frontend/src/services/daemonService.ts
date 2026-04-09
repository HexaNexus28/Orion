import { apiClient } from './api';
import { ENDPOINTS } from '../config/endpoints';
import type {
  ApiResponse,
  DaemonActionRequest,
  DaemonActionResponse,
  DaemonStatus,
  DaemonToolInfo,
} from '../types/dto';

// Daemon Service - Matches DaemonController
export const daemonService = {
  async getStatus(): Promise<ApiResponse<DaemonStatus>> {
    const response = await apiClient.get<ApiResponse<DaemonStatus>>(ENDPOINTS.daemon.status);
    return response.data;
  },

  async executeAction(request: DaemonActionRequest): Promise<ApiResponse<DaemonActionResponse>> {
    const response = await apiClient.post<ApiResponse<DaemonActionResponse>>(
      ENDPOINTS.daemon.action,
      request
    );
    return response.data;
  },

  async getAvailableTools(): Promise<ApiResponse<DaemonToolInfo[]>> {
    const response = await apiClient.get<ApiResponse<DaemonToolInfo[]>>(ENDPOINTS.daemon.tools);
    return response.data;
  },
};
