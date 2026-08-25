export interface DeferredQueueBadgeProps {
  /** Actions encore vivantes : `pending` + `awaiting_confirmation`. */
  enAttente: number;
  /** Sous-ensemble qui attend un feu vert — c'est ce qui rend la pastille urgente. */
  aConfirmer: number;
  onOpen: () => void;
}
