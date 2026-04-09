import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';

interface SettingsOverlayProps {
  isOpen: boolean;
  onClose: () => void;
}

export const SettingsOverlay: React.FC<SettingsOverlayProps> = ({ isOpen, onClose }) => {
  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          className="fixed inset-0 z-30 flex items-center justify-center bg-black/60 backdrop-blur-sm"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          onClick={onClose}
        >
          <motion.div
            className="w-full max-w-md rounded-2xl bg-orion-darker border border-orion-accent/20"
            initial={{ scale: 0.9, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0.9, opacity: 0 }}
            transition={{ type: 'spring', damping: 30, stiffness: 300 }}
            onClick={e => e.stopPropagation()}
          >
            <div className="p-6">
              <div className="flex items-center justify-between mb-6">
                <h2 className="text-xl font-semibold text-orion-text">Paramètres</h2>
                <button onClick={onClose} className="text-orion-textDim hover:text-orion-text">
                  <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>

              <div className="space-y-4">
                <div className="flex items-center justify-between p-4 rounded-lg bg-orion-dark/50">
                  <span className="text-orion-text">Heure du briefing</span>
                  <input
                    type="time"
                    defaultValue="07:00"
                    className="bg-orion-dark border border-orion-accent/20 rounded px-2 py-1 text-orion-text"
                  />
                </div>

                <div className="flex items-center justify-between p-4 rounded-lg bg-orion-dark/50">
                  <span className="text-orion-text">Mode sombre</span>
                  <div className="w-12 h-6 bg-orion-accent rounded-full relative">
                    <div className="absolute right-1 top-1 w-4 h-4 bg-white rounded-full" />
                  </div>
                </div>

                <div className="p-4 rounded-lg bg-orion-dark/50">
                  <span className="text-orion-text block mb-2">Provider LLM</span>
                  <select className="w-full bg-orion-dark border border-orion-accent/20 rounded px-3 py-2 text-orion-text">
                    <option>Ollama (local)</option>
                    <option>Claude API (cloud)</option>
                    <option>Auto (fallback)</option>
                  </select>
                </div>

                <div className="p-4 rounded-lg bg-orion-dark/50">
                  <span className="text-orion-text block mb-2">Modèle Ollama</span>
                  <select className="w-full bg-orion-dark border border-orion-accent/20 rounded px-3 py-2 text-orion-text">
                    <option>qwen2.5:14b</option>
                    <option>llama3.2:latest</option>
                    <option>mistral:latest</option>
                  </select>
                </div>
              </div>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
};
