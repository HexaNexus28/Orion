import axios, { AxiosInstance } from 'axios';
import { axiosConfig } from '../config/endpoints';
import { authService } from './authService';
import type { ApiResponse } from '../types';

// Create axios instance with default config
const apiClient: AxiosInstance = axios.create(axiosConfig);

/**
 * Jeton attache ICI, en un seul endroit. Le faire service par service garantirait qu'on
 * l'oublie quelque part — et un appel sans jeton part en 401 sans que la cause soit evidente.
 */
apiClient.interceptors.request.use((config) => {
  const token = authService.getToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Add response interceptor for error handling
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response) {
      // 401 = session absente ou expiree. On purge le jeton mort et on previent l'application :
      // sans ca l'utilisateur verrait « erreur » partout sans comprendre qu'il doit se reconnecter.
      if (error.response.status === 401) {
        authService.logout();
        window.dispatchEvent(new CustomEvent('orion:unauthenticated'));
        throw new Error('Session expiree — reconnexion necessaire');
      }

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
