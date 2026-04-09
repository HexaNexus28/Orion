import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useBriefing } from '../../services/briefingService';

interface BriefingOverlayProps {
  isOpen: boolean;
  onClose: () => void;
}

export const BriefingOverlay: React.FC<BriefingOverlayProps> = ({ isOpen, onClose }) => {
  const { briefing, loading, fetchToday } = useBriefing();

  // Fetch when opening
  React.useEffect(() => {
    if (isOpen) fetchToday();
  }, [isOpen, fetchToday]);

  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          className="fixed inset-0 z-30 flex items-start justify-center bg-black/60 backdrop-blur-sm"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          onClick={onClose}
        >
          <motion.div
            className="w-full max-w-md rounded-b-2xl bg-orion-darker border border-orion-accent/20"
            initial={{ y: '-100%' }}
            animate={{ y: 0 }}
            exit={{ y: '-100%' }}
            transition={{ type: 'spring', damping: 30, stiffness: 300 }}
            onClick={(e: React.MouseEvent<HTMLDivElement>) => e.stopPropagation()}
          >
            <div className="p-6">
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-xl font-semibold text-orion-text">Briefing du jour</h2>
                <button onClick={onClose} className="text-orion-textDim hover:text-orion-text">
                  <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>

              {/* Drag handle at bottom */}
              <div className="flex justify-center mt-4">
                <div className="w-10 h-1 rounded-full bg-orion-accent/30" />
              </div>

              {loading ? (
                <div className="text-center py-8 text-orion-textDim">Chargement...</div>
              ) : briefing ? (
                <div className="space-y-4">
                  {briefing.shiftStarStats && (
                    <div className="p-4 rounded-lg bg-orion-dark/50">
                      <h3 className="text-sm font-medium text-orion-accent mb-2">ShiftStar</h3>
                      <div className="grid grid-cols-3 gap-2 text-center">
                        <div>
                          <div className="text-lg font-semibold text-orion-text">{briefing.shiftStarStats.votes}</div>
                          <div className="text-xs text-orion-textDim">Votes</div>
                        </div>
                        <div>
                          <div className="text-lg font-semibold text-orion-text">{briefing.shiftStarStats.rating.toFixed(1)}</div>
                          <div className="text-xs text-orion-textDim">Note</div>
                        </div>
                        <div>
                          <div className="text-lg font-semibold text-orion-text">${briefing.shiftStarStats.mrr}</div>
                          <div className="text-xs text-orion-textDim">MRR</div>
                        </div>
                      </div>
                    </div>
                  )}

                  {briefing.calendarEvents && briefing.calendarEvents.length > 0 && (
                    <div className="p-4 rounded-lg bg-orion-dark/50">
                      <h3 className="text-sm font-medium text-orion-accent mb-2">Agenda</h3>
                      {briefing.calendarEvents.map((event: { time: string; title: string }, i: number) => (
                        <div key={i} className="flex items-center gap-2 text-sm text-orion-text">
                          <span className="text-orion-textDim">{event.time}</span>
                          <span>{event.title}</span>
                        </div>
                      ))}
                    </div>
                  )}

                  <div className="p-4 rounded-lg bg-orion-dark/50">
                    <h3 className="text-sm font-medium text-orion-accent mb-2">Résumé</h3>
                    <p className="text-sm text-orion-text whitespace-pre-wrap">{briefing.summary}</p>
                  </div>
                </div>
              ) : (
                <div className="text-center py-8 text-orion-textDim">
                  Aucun briefing disponible
                </div>
              )}
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
};
