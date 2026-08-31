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
  /** Carte du HUD produite par l'outil (type = 'tool_result'), absente la plupart du temps. */
  card?: HudCard;
  /** Numéro d'itération de la boucle, à partir de 1. */
  iteration: number;
}

// ── HUD ────────────────────────────────────────────────────────────────────
// Miroir de Orion.Core/DTOs/Responses/HudCard.cs — garder les deux alignés.
// Le raisonnement (gravité vs couleur, durées de vie, actions = appels d'outil) est documenté
// une seule fois, côté C#.

export type HudCardKind = 'metric' | 'status' | 'list' | 'sources';

export type HudCardState = 'neutral' | 'ok' | 'warn' | 'critical';

export type HudCardLifetime = 'pinned' | 'live' | 'event';

export interface HudCardAction {
  label: string;
  tool: string;
  /** Arguments JSON sérialisés, tels que le backend les a préparés. */
  arguments?: string;
}

export interface HudCardItem {
  label: string;
  value?: string;
  /** Lien externe : le front en fait un élément ouvrable. */
  url?: string;
}

/**
 * `id` est STABLE et porte le sujet (`git.ShiftStar`) : rappeler le même outil MET À JOUR la
 * carte au lieu d'en empiler une seconde.
 */
export interface HudCard {
  id: string;
  kind: HudCardKind;
  label: string;
  value?: string;
  unit?: string;
  state: HudCardState;
  items?: HudCardItem[];
  /** Absent = 'live' : une carte ne devient permanente que si elle le déclare. */
  lifetime?: HudCardLifetime;
  /** Absent = carte en lecture seule. */
  actions?: HudCardAction[];
  producedAt?: string;
}

/** Trace d'un outil exécuté pendant un tour — ce qu'ORION a FAIT, pas seulement dit. */
export interface ToolActivity {
  tool: string;
  args?: string;
  status: 'running' | 'ok' | 'failed';
  summary?: string;
  iteration: number;
}
