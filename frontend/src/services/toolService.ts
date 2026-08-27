import apiClient from './api';
import { ENDPOINTS } from '../config/endpoints';
import type { ApiResponse } from '../types';

/**
 * Exécution d'un outil demandée par l'interface.
 *
 * Passe par apiClient, donc par l'intercepteur de session : un jeton périmé déclenche la
 * reconnexion au lieu d'un échec muet sur le bouton.
 *
 * Le backend route l'appel vers ToolInvoker — même chemin que le modèle. Un outil irréversible
 * n'est donc PAS exécuté ici : il part en file d'attente, et la réponse le dit. Le bouton doit
 * refléter cette distinction plutôt que de prétendre au succès.
 */
export const toolService = {
  async invoke(tool: string, argumentsJson?: string): Promise<ApiResponse<unknown>> {
    const body = argumentsJson ? JSON.parse(argumentsJson) : {};
    const { data } = await apiClient.post<ApiResponse<unknown>>(`${ENDPOINTS.tools.invoke(tool)}`, body);
    return data;
  },
};
