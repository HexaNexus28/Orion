import { useEffect, useCallback, useState } from 'react';
import { apiClient } from '../services/api';
import { ENDPOINTS, API_BASE } from '../config/endpoints';

export interface OrionNotification {
  type: 'info' | 'warning' | 'alert' | 'proactive';
  title: string;
  message: string;
  priority: 'low' | 'normal' | 'high' | 'critical';
  timestamp: string;
  speak?: boolean;
  metadata?: Record<string, unknown>;
}

interface ConnectionStatus {
  clientId: string;
  timestamp: string;
}

interface FrontendActionRequest {
  action: string;
  parameter?: string;
  data?: Record<string, unknown>;
}

export const useOrionNotifications = () => {
  const [lastNotification, setLastNotification] = useState<OrionNotification | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [clientId, setClientId] = useState<string | null>(null);

  // Sélection de la meilleure voix française disponible
  const getBestFrenchVoice = useCallback((): SpeechSynthesisVoice | undefined => {
    const voices = window.speechSynthesis.getVoices();
    const fr = voices.filter(v => v.lang.startsWith('fr'));
    if (!fr.length) return undefined;
    // 1. Voix neurales Windows Edge
    const natural = fr.find(v => v.name.includes('Natural') || v.name.includes('Eva') || v.name.includes('Denise') || v.name.includes('Elsa'));
    if (natural) return natural;
    // 2. Google Français
    const google = fr.find(v => v.name.includes('Google'));
    if (google) return google;
    // 3. N'importe sauf Hortense
    return fr.find(v => !v.name.includes('Hortense')) ?? fr[0];
  }, []);

  // Synthèse vocale via Web Speech API
  const speak = useCallback((text: string) => {
    if (!('speechSynthesis' in window)) {
      console.warn('[useOrionNotifications] Web Speech API not supported');
      return;
    }

    window.speechSynthesis.cancel();

    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = 'fr-FR';
    utterance.rate = 0.92;
    utterance.pitch = 1.0;
    utterance.volume = 1;

    const voice = getBestFrenchVoice();
    if (voice) utterance.voice = voice;

    window.speechSynthesis.speak(utterance);
  }, [getBestFrenchVoice]);

  // Envoyer une action au daemon via le backend (utilise axios + endpoints)
  const sendAction = useCallback(async (action: string, parameter?: string, data?: Record<string, unknown>) => {
    try {
      const request: FrontendActionRequest = { action, parameter, data };
      const response = await apiClient.post(ENDPOINTS.notifications.action, request);
      return response.data.success;
    } catch (err) {
      console.error('[useOrionNotifications] Failed to send action:', err);
      return false;
    }
  }, []);

  // Parler via le daemon (option B - TTS local Windows)
  const speakViaDaemon = useCallback(async (text: string) => {
    return sendAction('speak', text);
  }, [sendAction]);

  useEffect(() => {
    const eventSource = new EventSource(`${API_BASE}/api/proactivenotification/stream`);

    eventSource.addEventListener('connected', (event) => {
      const data: ConnectionStatus = JSON.parse(event.data);
      setClientId(data.clientId);
      setIsConnected(true);
      console.log('[useOrionNotifications] Connected:', data.clientId);
    });

    eventSource.addEventListener('notification', (event) => {
      try {
        const notification: OrionNotification = JSON.parse(event.data);
        setLastNotification(notification);
        console.log('[useOrionNotifications] Received:', notification.title);

        // Parler si demandé
        if (notification.speak && notification.message) {
          speak(notification.message);
        }
      } catch (err) {
        console.error('[useOrionNotifications] Failed to parse notification:', err);
      }
    });

    eventSource.addEventListener('heartbeat', () => {
      // Connection alive
    });

    eventSource.onerror = () => {
      setIsConnected(false);
      console.error('[useOrionNotifications] SSE connection lost');
    };

    return () => {
      eventSource.close();
      setIsConnected(false);
    };
  }, [speak]);

  return {
    lastNotification,
    isConnected,
    clientId,
    speak,
    sendAction,
    speakViaDaemon
  };
};

const getBestFrenchVoiceGlobal = (): SpeechSynthesisVoice | undefined => {
  const voices = window.speechSynthesis.getVoices();
  const fr = voices.filter(v => v.lang.startsWith('fr'));
  if (!fr.length) return undefined;
  const natural = fr.find(v => v.name.includes('Natural') || v.name.includes('Eva') || v.name.includes('Denise') || v.name.includes('Elsa'));
  if (natural) return natural;
  const google = fr.find(v => v.name.includes('Google'));
  if (google) return google;
  return fr.find(v => !v.name.includes('Hortense')) ?? fr[0];
};

// Hook simple pour parler directement
export const useOrionSpeech = () => {
  const speak = useCallback((text: string) => {
    if (!('speechSynthesis' in window)) {
      console.warn('[useOrionSpeech] Web Speech API not supported');
      return;
    }

    window.speechSynthesis.cancel();

    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = 'fr-FR';
    utterance.rate = 0.92;
    utterance.pitch = 1.0;
    utterance.volume = 1;

    const voice = getBestFrenchVoiceGlobal();
    if (voice) utterance.voice = voice;

    window.speechSynthesis.speak(utterance);
  }, []);

  const stop = useCallback(() => {
    if ('speechSynthesis' in window) {
      window.speechSynthesis.cancel();
    }
  }, []);

  return { speak, stop };
};
