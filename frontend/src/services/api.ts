import axios, { AxiosInstance } from 'axios';
import { axiosConfig } from '../config/endpoints';
import type { ApiResponse } from '../types/dto';

// Create axios instance with default config
const apiClient: AxiosInstance = axios.create(axiosConfig);

// Add response interceptor for error handling
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response) {
      // Server responded with error status
      const data = error.response.data as ApiResponse<unknown>;
      throw new Error(data.message || `HTTP ${error.response.status}`);
    } else if (error.request) {
      // Request made but no response
      throw new Error('Network error: No response from server');
    } else {
      // Error in request setup
      throw new Error(error.message);
    }
  }
);

export { apiClient };
export default apiClient;
