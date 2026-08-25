import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import type { DeferredActionDto } from '../../types';
import type { DeferredQueueOverlayProps } from '../../props/DeferredQueueOverlay.props';

/** Ce que l'utilisateur avait demandé, tel qu'il l'avait dit — à défaut, le nom de l'outil. */
const libelle = (action: DeferredActionDto): string =>
  action.requestedBy?.trim() || action.toolName;

const restant = (expiresAt: string): string => {
  const minutes = Math.round((new Date(expiresAt).getTime() - Date.now()) / 60000);
  if (minutes <= 0) return 'expire maintenant';
  if (minutes < 60) return `expire dans ${minutes} min`;
  return `expire dans ${Math.round(minutes / 60)} h`;
};

const ETIQUETTES: Record<DeferredActionDto['status'], string> = {
  pending: 'en attente',
  awaiting_confirmation: 'ton feu vert',
  executed: 'fait',
  failed: 'échec',
  expired: 'expirée',
  cancelled: 'annulée',
};

export const DeferredQueueOverlay: React.FC<DeferredQueueOverlayProps> = ({ isOpen, onClose, queue }) => {
  const { actions, loading, error, confirm, cancel } = queue;

  const aConfirmer = actions.filter(a => a.status === 'awaiting_confirmation');
  const enAttente = actions.filter(a => a.status === 'pending');
  const passees = actions.filter(a => a.status !== 'pending' && a.status !== 'awaiting_confirmation');

  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          className="fixed inset-0 z-30 flex items-end justify-center bg-black/60 backdrop-blur-sm"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          onClick={onClose}
        >
          <motion.div
            className="w-full max-w-2xl max-h-[80vh] overflow-hidden rounded-t-2xl bg-orion-darker border border-orion-accent/20"
            initial={{ y: '100%' }}
            animate={{ y: 0 }}
            exit={{ y: '100%' }}
            transition={{ type: 'spring', damping: 30, stiffness: 300 }}
            onClick={e => e.stopPropagation()}
          >
            <div className="flex justify-center pt-3 pb-1">
              <div className="w-10 h-1 rounded-full bg-orion-accent/30" />
            </div>

            <div className="p-6">
              <div className="flex items-center justify-between mb-2">
                <h2 className="text-xl font-semibold text-orion-text">En attente de ton PC</h2>
                <button onClick={onClose} className="text-orion-textDim hover:text-orion-text">
                  <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>
              <p className="text-xs text-orion-textDim mb-6">
                Ces actions partiront au réveil de ta machine. Elles expirent au bout de 24 h.
              </p>

              {error && (
                <div className="mb-4 p-3 rounded-lg bg-red-500/10 border border-red-500/30 text-sm text-red-300">
                  {error}
                </div>
              )}

              {loading && actions.length === 0 ? (
                <div className="text-center py-8 text-orion-textDim">Chargement...</div>
              ) : (
                <div className="space-y-6 max-h-[55vh] overflow-y-auto">
                  {aConfirmer.length > 0 && (
                    <section>
                      <h3 className="text-sm font-medium text-orion-accent mb-3">
                        Ton PC est revenu — je redemande avant de lancer
                      </h3>
                      <div className="space-y-3">
                        {aConfirmer.map(action => (
                          <div
                            key={action.id}
                            className="p-4 rounded-lg bg-orion-dark/50 border border-orion-accent/30"
                          >
                            <p className="text-orion-text text-sm">{libelle(action)}</p>
                            <p className="mt-1 text-xs text-orion-textDim">
                              {action.toolName} · modifie l'état de la machine · {restant(action.expiresAt)}
                            </p>
                            <div className="mt-3 flex gap-2">
                              <button
                                onClick={() => void confirm(action.id)}
                                className="px-3 py-1.5 rounded-md bg-orion-accent/20 border border-orion-accent/40 text-xs text-orion-text hover:bg-orion-accent/30"
                              >
                                Lancer maintenant
                              </button>
                              <button
                                onClick={() => void cancel(action.id)}
                                className="px-3 py-1.5 rounded-md border border-orion-accent/20 text-xs text-orion-textDim hover:text-orion-text"
                              >
                                Laisser tomber
                              </button>
                            </div>
                          </div>
                        ))}
                      </div>
                    </section>
                  )}

                  {enAttente.length > 0 && (
                    <section>
                      <h3 className="text-sm font-medium text-orion-textDim mb-3">
                        Partira dès que ton PC se rallume
                      </h3>
                      <div className="space-y-3">
                        {enAttente.map(action => (
                          <div
                            key={action.id}
                            className="p-4 rounded-lg bg-orion-dark/50 border border-orion-accent/10"
                          >
                            <div className="flex items-start justify-between gap-3">
                              <div className="min-w-0">
                                <p className="text-orion-text text-sm truncate">{libelle(action)}</p>
                                <p className="mt-1 text-xs text-orion-textDim">
                                  {action.toolName}
                                  {action.isDestructive && ' · te sera reconfirmée'}
                                  {' · '}
                                  {restant(action.expiresAt)}
                                </p>
                              </div>
                              <button
                                onClick={() => void cancel(action.id)}
                                className="shrink-0 px-3 py-1.5 rounded-md border border-orion-accent/20 text-xs text-orion-textDim hover:text-orion-text"
                              >
                                Annuler
                              </button>
                            </div>
                          </div>
                        ))}
                      </div>
                    </section>
                  )}

                  {passees.length > 0 && (
                    <section>
                      <h3 className="text-sm font-medium text-orion-textDim mb-3">Récemment</h3>
                      <div className="space-y-2">
                        {passees.map(action => (
                          <div key={action.id} className="flex items-center gap-2 text-xs text-orion-textDim">
                            <span className="text-orion-text">{action.toolName}</span>
                            <span>·</span>
                            <span>{ETIQUETTES[action.status]}</span>
                            {action.error && <span className="truncate">· {action.error}</span>}
                          </div>
                        ))}
                      </div>
                    </section>
                  )}

                  {actions.length === 0 && !loading && (
                    <div className="text-center py-8 text-orion-textDim">
                      Rien en attente — tout ce que tu m'as demandé est fait.
                    </div>
                  )}
                </div>
              )}
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
};
