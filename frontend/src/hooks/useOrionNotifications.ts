import { authService } from '../services/authService';
import { useHudCards } from '../context/HudCardsContext';
import type { HudCard } from '../types/dto/agentDto';
import { useEffect, useCallback, useState } from 'react';
import { apiClient } from '../services/api';
import { ENDPOINTS, API_BASE } from '../config/endpoints';

export interface OrionNotification {
  /** `briefing` et `deferred` sont émis par le backend (BriefingScheduler,
   *  DeferredActionWatcher) — ils manquaient à cette union. */
  type: 'info' | 'warning' | 'alert' | 'proactive' | 'briefing' | 'deferred';
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

  const { upsertCard } = useHudCards();

  useEffect(() => {
    // Reconnexion REPRISE A LA MAIN, et ce n est pas un raffinement.
    //
    // EventSource se reconnecte tout seul — sur la MEME URL, donc avec le meme billet. Un
    // billet vivant 60 secondes, cette reconnexion automatique echouerait indefiniment des la
    // premiere coupure : le flux serait mort sans qu aucune erreur ne le dise. On ferme donc
    // nous-memes et on rouvre avec un billet FRAIS.
    let source: EventSource | null = null;
    let minuteur: ReturnType<typeof setTimeout> | null = null;
    let echecs = 0;
    let abandonne = false;

    const ouvrir = async () => {
      if (abandonne) return;
      try {
        const billet = await authService.getStreamTicket();
        if (abandonne) return;

        source = new EventSource(
          `${API_BASE}/api/proactivenotification/stream?access_token=${encodeURIComponent(billet)}`
        );

        source.addEventListener("connected", (event) => {
          const data: ConnectionStatus = JSON.parse((event as MessageEvent).data);
          setClientId(data.clientId);
          setIsConnected(true);
          echecs = 0;   // palier remis a zero seulement sur une connexion REELLEMENT etablie
          console.log("[useOrionNotifications] Connecte :", data.clientId);
        });

        source.addEventListener("notification", (event) => {
          try {
            const notification: OrionNotification = JSON.parse((event as MessageEvent).data);
            setLastNotification(notification);
            if (notification.speak && notification.message) {
              speak(notification.message);
            }
          } catch (err) {
            console.error("[useOrionNotifications] Notification illisible :", err);
          }
        });

        // Cartes PERMANENTES poussees par le serveur (HudBroadcastService), independamment de
        // toute conversation. Meme magasin et meme identifiant stable que les cartes issues des
        // outils : une carte poussee remplace simplement celle de meme id, d ou qu elle vienne.
        source.addEventListener("card", (event) => {
          try {
            upsertCard(JSON.parse((event as MessageEvent).data) as HudCard);
          } catch (err) {
            console.error("[useOrionNotifications] Carte illisible :", err);
          }
        });

        source.addEventListener("heartbeat", () => { /* connexion vivante */ });

        source.onerror = () => {
          setIsConnected(false);
          source?.close();   // sinon EventSource retenterait seul, avec le billet perime
          source = null;
          replanifier();
        };
      } catch (err) {
        // Billet refuse = session invalide. assertSessionValide ne s applique pas ici (axios
        // nu), mais l intercepteur d apiClient s en chargera au premier appel normal.
        console.warn("[useOrionNotifications] Billet de flux indisponible :", err);
        setIsConnected(false);
        replanifier();
      }
    };

    const replanifier = () => {
      if (abandonne || minuteur) return;
      echecs += 1;
      // Palier plafonne a 30 s : une session invalide ne se repare pas toute seule, inutile
      // de marteler le serveur toutes les secondes en attendant.
      const delai = Math.min(2000 * 2 ** (echecs - 1), 30000);
      minuteur = setTimeout(() => { minuteur = null; void ouvrir(); }, delai);
    };

    void ouvrir();

    return () => {
      abandonne = true;
      if (minuteur) clearTimeout(minuteur);
      source?.close();
      setIsConnected(false);
    };
  }, [speak, upsertCard]);

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
