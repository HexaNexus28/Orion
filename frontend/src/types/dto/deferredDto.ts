// Miroir de Orion.Core.DTOs.Responses.DeferredActionDto

/**
 * Deux états seulement sont vivants : `pending` et `awaiting_confirmation`.
 * Ce sont les seuls que l'utilisateur peut encore annuler.
 */
export type DeferredStatus =
  | 'pending'
  | 'awaiting_confirmation'
  | 'executed'
  | 'failed'
  | 'expired'
  | 'cancelled';

export interface DeferredActionDto {
  id: string;
  toolName: string;
  /** Arguments JSON tels que le modèle les a produits. */
  arguments: string;
  status: DeferredStatus;
  /** Figé à l'enfilement : une action destructive se redemande avant de partir. */
  isDestructive: boolean;
  origin: 'chat' | 'proactive';
  /** La phrase exacte de l'utilisateur, quand elle est connue. */
  requestedBy: string | null;
  requestedAt: string;
  expiresAt: string;
  resolvedAt: string | null;
  result: string | null;
  error: string | null;
}
