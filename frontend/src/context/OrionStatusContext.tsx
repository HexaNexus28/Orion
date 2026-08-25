import { createContext, useContext, useState, ReactNode, useCallback, useEffect } from 'react';
import { healthService } from '../services/healthService';
import { daemonService } from '../services/daemonService';
import type { LLMProvider } from '../types/dto/chatDto';

interface OrionStatus {
  llmOnline: boolean;
  daemonConnected: boolean;
  /** Miroir de l'enum backend Orion.Core.Enums.LLMProvider. */
  activeProvider: LLMProvider | null;
  /** Modèle réellement actif, tel que rapporté par le backend. */
  activeModel: string | null;
  lastPing: number;
}

interface OrionStatusContextType extends OrionStatus {
  ping: () => Promise<void>;
}

const OrionStatusContext = createContext<OrionStatusContextType | null>(null);

export const OrionStatusProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [status, setStatus] = useState<OrionStatus>({
    llmOnline: false,
    daemonConnected: false,
    activeProvider: null,
    activeModel: null,
    lastPing: 0
  });

  const ping = useCallback(async () => {
    try {
      // Passer par la couche service (typée) plutôt que par apiClient en direct :
      // les appels bruts renvoyaient du `any` implicite et dupliquaient healthService
      // et daemonService, qui existaient déjà mais n'étaient référencés nulle part.
      const [health, daemon] = await Promise.all([
        healthService.getHealth(),
        daemonService.getStatus(),
      ]);

      // Le backend renvoie le nom de l'enum tel quel ("Nim", "Ollama", "None").
      const rawProvider = health.data?.llmProvider ?? 'None';
      const activeProvider: LLMProvider | null =
        rawProvider === 'Nim' || rawProvider === 'Ollama' ? rawProvider : null;

      setStatus({
        llmOnline: activeProvider !== null,
        daemonConnected: daemon.data?.connected === true,
        activeProvider,
        activeModel: health.data?.llmModel ?? null,
        lastPing: Date.now()
      });
    } catch {
      setStatus(prev => ({
        ...prev,
        llmOnline: false,
        daemonConnected: false,
        activeModel: null,
        lastPing: Date.now()
      }));
    }
  }, []);

  useEffect(() => {
    ping();
    const interval = setInterval(ping, 30000); // Ping every 30s
    return () => clearInterval(interval);
  }, [ping]);

  return (
    <OrionStatusContext.Provider value={{ ...status, ping }}>
      {children}
    </OrionStatusContext.Provider>
  );
};

export const useOrionStatus = () => {
  const context = useContext(OrionStatusContext);
  if (!context) {
    throw new Error('useOrionStatus must be used within OrionStatusProvider');
  }
  return context;
};
