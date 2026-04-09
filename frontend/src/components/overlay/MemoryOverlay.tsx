import React, { useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useMemory } from '../../services/memoryService';

interface MemoryOverlayProps {
  isOpen: boolean;
  onClose: () => void;
}

export const MemoryOverlay: React.FC<MemoryOverlayProps> = ({ isOpen, onClose }) => {
  const { memories, loading, search } = useMemory();
  const [query, setQuery] = useState('');

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    if (query.trim()) {
      search(query);
    }
  };

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
            {/* Drag handle */}
            <div className="flex justify-center pt-3 pb-1">
              <div className="w-10 h-1 rounded-full bg-orion-accent/30" />
            </div>

            <div className="p-6">
              <div className="flex items-center justify-between mb-6">
                <h2 className="text-xl font-semibold text-orion-text">Mémoire ORION</h2>
                <button onClick={onClose} className="text-orion-textDim hover:text-orion-text">
                  <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>

              <form onSubmit={handleSearch} className="mb-4">
                <input
                  type="text"
                  value={query}
                  onChange={e => setQuery(e.target.value)}
                  placeholder="Rechercher dans la mémoire..."
                  className="w-full p-3 rounded-lg bg-orion-dark border border-orion-accent/20 text-orion-text placeholder-orion-textDim focus:border-orion-accent/50 outline-none"
                />
              </form>

              {loading ? (
                <div className="text-center py-8 text-orion-textDim">Chargement...</div>
              ) : (
                <div className="space-y-3 max-h-[50vh] overflow-y-auto">
                  {memories.map(memory => (
                    <div
                      key={memory.id}
                      className="p-4 rounded-lg bg-orion-dark/50 border border-orion-accent/10"
                    >
                      <p className="text-orion-text text-sm">{memory.content}</p>
                      <div className="mt-2 flex items-center gap-2 text-xs text-orion-textDim">
                        <span>{new Date(memory.createdAt).toLocaleDateString('fr-FR')}</span>
                        <span>•</span>
                        <span>Similarité: {memory.similarity.toFixed(2)}</span>
                      </div>
                    </div>
                  ))}
                  {memories.length === 0 && !loading && (
                    <div className="text-center py-8 text-orion-textDim">
                      Aucun souvenir trouvé
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
