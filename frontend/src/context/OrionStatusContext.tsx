import { createContext, useContext, useState, ReactNode, useCallback, useEffect } from 'react';
import { apiClient } from '../services/api';
import { ENDPOINTS } from '../config/endpoints';

interface OrionStatus {
  llmOnline: boolean;
  daemonConnected: boolean;
  activeProvider: 'ollama' | 'claude' | null;
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
    lastPing: 0
  });

  const ping = useCallback(async () => {
    try {
      const [healthResponse, daemonResponse] = await Promise.all([
        apiClient.get(ENDPOINTS.health),
        apiClient.get(ENDPOINTS.daemon.status),
      ]);

      const health = healthResponse.data?.data;
      const daemon = daemonResponse.data?.data;
      const rawProvider = health?.llmProvider?.toLowerCase?.() ?? 'none';
      const activeProvider = rawProvider === 'ollama'
        ? 'ollama'
        : rawProvider === 'anthropic'
          ? 'claude'
          : null;

      setStatus({
        llmOnline: rawProvider !== 'none',
        daemonConnected: daemon?.connected === true,
        activeProvider,
        lastPing: Date.now()
      });
    } catch {
      setStatus(prev => ({
        ...prev,
        llmOnline: false,
        daemonConnected: false,
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
