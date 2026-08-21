import { useCallback, useEffect, useState } from 'react';
import { apiClient } from './api';
import { ENDPOINTS } from '../config/endpoints';
import type { ApiResponse, DeferredActionDto } from '../types';

/**
 * La file des actions qu'ORION n'a pas pu exécuter parce que le PC était éteint.
 *
 * Elle DOIT être visible et annulable : une file invisible est une promesse qu'on ne peut plus
 * reprendre, et ORION finirait par exécuter au réveil des choses oubliées depuis longtemps.
 */
class DeferredService {
  async getQueue(): Promise<ApiResponse<DeferredActionDto[]>> {
    const response = await apiClient.get<ApiResponse<DeferredActionDto[]>>(ENDPOINTS.deferred.queue);
    return response.data;
  }

  async confirm(id: string): Promise<ApiResponse<DeferredActionDto>> {
    const response = await apiClient.post<ApiResponse<DeferredActionDto>>(ENDPOINTS.deferred.confirm(id));
    return response.data;
  }

  async cancel(id: string): Promise<ApiResponse<DeferredActionDto>> {
    const response = await apiClient.post<ApiResponse<DeferredActionDto>>(ENDPOINTS.deferred.cancel(id));
    return response.data;
  }
}

export const deferredService = new DeferredService();

const EN_ATTENTE: DeferredActionDto['status'][] = ['pending', 'awaiting_confirmation'];

/**
 * La file est lue UNE seule fois, dans App, puis passée au badge et à l'overlay.
 * Deux instances du hook feraient deux appels, et surtout : après une confirmation dans
 * l'overlay, le badge continuerait d'afficher un compteur périmé.
 */
export interface DeferredQueue {
  actions: DeferredActionDto[];
  enAttente: DeferredActionDto[];
  aConfirmer: DeferredActionDto[];
  loading: boolean;
  error: string | null;
  refresh: () => Promise<void>;
  confirm: (id: string) => Promise<void>;
  cancel: (id: string) => Promise<void>;
}

export const useDeferredQueue = (): DeferredQueue => {
  const [actions, setActions] = useState<DeferredActionDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await deferredService.getQueue();
      if (response.success && response.data) {
        setActions(response.data);
      } else {
        setError(response.message ?? 'File indisponible');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur inconnue');
    } finally {
      setLoading(false);
    }
  }, []);

  // Une action confirmée ou annulée change d'état côté serveur : on relit plutôt que de
  // recopier l'état localement, sinon l'UI et la base divergent au premier échec.
  const confirm = useCallback(async (id: string) => {
    try {
      await deferredService.confirm(id);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Confirmation impossible');
    } finally {
      await refresh();
    }
  }, [refresh]);

  const cancel = useCallback(async (id: string) => {
    try {
      await deferredService.cancel(id);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Annulation impossible');
    } finally {
      await refresh();
    }
  }, [refresh]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const enAttente = actions.filter(a => EN_ATTENTE.includes(a.status));
  const aConfirmer = actions.filter(a => a.status === 'awaiting_confirmation');

  return { actions, enAttente, aConfirmer, loading, error, refresh, confirm, cancel };
};
