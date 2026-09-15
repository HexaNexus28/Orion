import { useState, useCallback, useRef, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import type { SlideInputProps } from '../../types';

/**
 * SlideInput — saisie TEXTE, qui glisse depuis le bas.
 *
 * Cachée par défaut, apparaît au tap sur l'entité, disparaît après envoi.
 *
 * PAS DE MICRO ICI, et c'est délibéré. Ce panneau portait un second pipeline vocal COMPLET —
 * sa propre capture (ScriptProcessorNode, API dépréciée), son propre encodage WAV, son propre
 * endpoint HTTP `/api/voice/transcribe` — parallèle au WebSocket full-duplex de l'écran
 * principal. Deux implémentations de « parler à ORION », avec des latences et des bugs
 * différents : corriger l'une ne corrigeait jamais l'autre.
 *
 * La voix a un seul chemin désormais : le VAD Silero de l'écran principal vers `/ws/voice`.
 */
export const SlideInput: React.FC<SlideInputProps> = ({
  isVisible,
  onSubmit,
  disabled = false,
  state,
  onClose
}) => {
  const [text, setText] = useState('');
  const inputRef = useRef<HTMLTextAreaElement>(null);

  // Focus automatique quand visible
  useEffect(() => {
    if (isVisible && inputRef.current) {
      inputRef.current.focus();
    }
  }, [isVisible]);

  // Fermer sur Escape
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isVisible) {
        onClose();
      }
    };
    window.addEventListener('keydown', handleEscape);
    return () => window.removeEventListener('keydown', handleEscape);
  }, [isVisible, onClose]);

  const handleSubmit = useCallback(() => {
    if (text.trim() && !disabled) {
      onSubmit(text.trim());
      setText('');
      onClose(); // Disparaît après envoi
    }
  }, [text, disabled, onSubmit, onClose]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit();
    }
  }, [handleSubmit]);

  const handleTextareaChange = useCallback((e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setText(e.target.value);
    // Auto-resize
    e.target.style.height = 'auto';
    e.target.style.height = `${Math.min(e.target.scrollHeight, 200)}px`;
  }, []);


  return (
    <AnimatePresence>
      {isVisible && (
        <>
          {/* Backdrop - click to close */}
          <motion.div
            className="fixed inset-0 bg-black/20 backdrop-blur-sm z-40"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={onClose}
          />

          {/* Input Container - slides up from bottom */}
          <motion.div
            className="fixed bottom-0 left-0 right-0 z-50 p-4 pb-8"
            initial={{ y: '100%' }}
            animate={{ y: 0 }}
            exit={{ y: '100%' }}
            transition={{ type: 'spring', damping: 25, stiffness: 300 }}
          >
            <div className="max-w-2xl mx-auto">
              <div className="relative flex items-end gap-2 p-4 rounded-2xl bg-orion-darker/95 backdrop-blur-xl border border-orion-accent/30 shadow-2xl shadow-orion-accent/10">
                {/* Text Input */}
                <textarea
                  ref={inputRef}
                  value={text}
                  onChange={handleTextareaChange}
                  onKeyDown={handleKeyDown}
                  disabled={disabled}
                  placeholder="Écris à ORION..."
                  className="flex-1 bg-transparent text-orion-text placeholder-orion-textDim resize-none outline-none min-h-[24px] max-h-[200px] font-sans text-lg"
                  rows={1}
                />

                {/* Send Button */}
                <motion.button
                  onClick={handleSubmit}
                  disabled={disabled || !text.trim()}
                  className="p-3 rounded-xl bg-orion-accent text-orion-dark disabled:opacity-30 disabled:cursor-not-allowed hover:brightness-110 transition-all"
                  whileTap={{ scale: 0.95 }}
                >
                  <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
                  </svg>
                </motion.button>
              </div>

              {/* State indicator */}
              <motion.div
                className="mt-3 text-center text-sm text-orion-textDim"
                animate={{ opacity: state === 'thinking' ? [0.5, 1, 0.5] : 0.6 }}
                transition={{ duration: 1.5, repeat: Infinity }}
              >
                {state === 'listening' && 'Écoute...'}
                {state === 'thinking' && 'ORION réfléchit...'}
                {state === 'responding' && 'Réponse en cours...'}
                {state === 'idle' && 'Appuyez sur Entrée pour envoyer, Échap pour fermer'}
              </motion.div>
            </div>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
};
