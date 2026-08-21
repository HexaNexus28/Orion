// ORION API Endpoints Configuration
// Matches backend Orion.Api.Controllers

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5107';

export const API_BASE = API_BASE_URL;

// Endpoints matching backend controllers
export const ENDPOINTS = {
  // ChatController - api/chat
  chat: {
    send: '/api/chat',
    stream: '/api/chat/stream',
    conversation: '/api/chat',  // GET /api/chat/{id}
    history: '/api/chat/history',
  },

  // DaemonController - api/daemon
  daemon: {
    status: '/api/daemon/status',
    action: '/api/daemon/action',
    tools: '/api/daemon/tools',
  },

  // HealthController - api/health
  health: '/api/health',

  // MemoryController - api/memory
  memory: {
    search: '/api/memory/search',
    list: '/api/memory',
    delete: (id: string) => `/api/memory/${id}`,
  },

  // BriefingController - api/briefing
  briefing: {
    today: '/api/briefing/today',
    history: '/api/briefing/history',
  },

  // VoiceController - api/voice (HTTP legacy)
  voice: {
    transcribe: '/api/voice/transcribe',
    synthesize: '/api/voice/synthesize',
    status: '/api/voice/status',
    converse: '/api/voice/converse',     // POST — legacy half-duplex pipeline
  },

  // WebSocket Voice — full-duplex pipeline (replaces /converse)
  voiceWS: '/ws/voice',

  // DeferredActionsController - api/deferred-actions
  // Ce qu'ORION n'a pas pu faire parce que le PC était éteint, et qu'il fera au réveil.
  deferred: {
    queue: '/api/deferred-actions',
    confirm: (id: string) => `/api/deferred-actions/${id}/confirm`,
    cancel: (id: string) => `/api/deferred-actions/${id}/cancel`,
  },

  // ProactiveNotificationController - notifications temps réel daemon → frontend
  notifications: {
    stream: '/api/proactivenotification/stream',  // SSE - Server-Sent Events
    notify: '/api/proactivenotification/notify',  // POST - Daemon envoie notification
    action: '/api/proactivenotification/action',  // POST - Frontend envoie action au daemon
  },
} as const;

// Axios default config
export const axiosConfig = {
  baseURL: API_BASE_URL,
  timeout: 120000,
  headers: {
    'Content-Type': 'application/json',
  },
};
