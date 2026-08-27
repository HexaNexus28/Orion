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

/** Forme de la carte : détermine le composant de rendu. */
export type HudCardKind = 'metric' | 'status' | 'list' | 'sources';

/**
 * Gravité, PAS une couleur. Le backend dit ce qui se passe, le front décide de
 * l'apparence — sinon changer le thème obligerait à modifier des outils métier.
 */
export type HudCardState = 'neutral' | 'ok' | 'warn' | 'critical';

/**
 * Durée de vie à l'écran — ce qui sépare un widget d'une notification.
 *
 * Sans cette distinction tout se valait : un panneau permanent et une carte d'outil occupaient
 * le même espace et disparaissaient ensemble au message suivant. Un HUD n'est pas un flux.
 */
export type HudCardLifetime = 'pinned' | 'live' | 'event';

export interface HudCardItem {
  label: string;
  value?: string;
  /** Lien externe : le front en fait un élément ouvrable. */
  url?: string;
}

/**
 * Carte produite par un OUTIL à partir de son résultat réel.
 *
 * Remplace parseHoloCards(), qui fabriquait des cartes par expression régulière sur la prose :
 * toute statistique en gras en devenait une, par accident, et rien n'apparaissait quand ça
 * comptait. Une carte est désormais la conséquence d'une action réellement exécutée.
 *
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
