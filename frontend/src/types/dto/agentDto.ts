// agentDto.ts — Événements émis par la boucle agent du backend (SSE /api/chat/stream)
// Miroir de Orion.Core/DTOs/Responses/AgentEvent.cs — garder les deux alignés.

export type AgentEventType = 'token' | 'tool_start' | 'tool_result' | 'done' | 'error';

export interface AgentEvent {
  type: AgentEventType;
  /** Texte du token (type = 'token') ou message d'erreur (type = 'error'). */
  text?: string;
  /** Nom de l'outil (types 'tool_start' et 'tool_result'). */
  tool?: string;
  /** Arguments JSON de l'appel (type = 'tool_start'). */
  args?: string;
  /** Succès de l'exécution (type = 'tool_result'). */
  ok?: boolean;
  /** Résultat tronqué, destiné à l'affichage (type = 'tool_result'). */
  summary?: string;
  /** Numéro d'itération de la boucle, à partir de 1. */
  iteration: number;
}

/** Trace d'un outil exécuté pendant un tour — ce qu'ORION a FAIT, pas seulement dit. */
export interface ToolActivity {
  tool: string;
  args?: string;
  status: 'running' | 'ok' | 'failed';
  summary?: string;
  iteration: number;
}
