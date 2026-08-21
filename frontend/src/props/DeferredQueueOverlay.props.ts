import type { DeferredQueue } from '../services/deferredService';

export interface DeferredQueueOverlayProps {
  isOpen: boolean;
  onClose: () => void;
  /** Lue une seule fois dans App, partagée avec la pastille — jamais deux instances du hook. */
  queue: DeferredQueue;
}
