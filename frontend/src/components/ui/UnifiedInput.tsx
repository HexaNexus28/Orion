import { useState, useCallback, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import type { SlideInputProps } from '../../types';

export const UnifiedInput: React.FC<SlideInputProps> = ({
  onSubmit,
  onVoiceStart,
  onVoiceEnd,
  disabled = false,
  state
}) => {
  const [text, setText] = useState('');
  const [isRecording, setIsRecording] = useState(false);
  const inputRef = useRef<HTMLTextAreaElement>(null);

  const handleSubmit = useCallback(() => {
    if (text.trim() && !disabled) {
      onSubmit(text.trim());
      setText('');
      if (inputRef.current) {
        inputRef.current.style.height = 'auto';
      }
    }
  }, [text, disabled, onSubmit]);

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

  const toggleVoice = useCallback(() => {
    if (isRecording) {
      setIsRecording(false);
      onVoiceEnd?.(''); // Placeholder - actual VAD implementation would return transcript
    } else {
      setIsRecording(true);
      onVoiceStart?.();
    }
  }, [isRecording, onVoiceStart, onVoiceEnd]);

  return (
    <div className="w-full max-w-2xl mx-auto">
      <div className="relative flex items-end gap-2 p-4 rounded-2xl bg-orion-darker border border-orion-accent/20 focus-within:border-orion-accent/50 transition-colors">
        {/* Text Input */}
        <textarea
          ref={inputRef}
          value={text}
          onChange={handleTextareaChange}
          onKeyDown={handleKeyDown}
          disabled={disabled || isRecording}
          placeholder={isRecording ? "Écoute en cours..." : "Parle à ORION..."}
          className="flex-1 bg-transparent text-orion-text placeholder-orion-textDim resize-none outline-none min-h-[24px] max-h-[200px] font-sans"
          rows={1}
        />

        {/* Voice Button */}
        <motion.button
          onClick={toggleVoice}
          disabled={disabled}
          className={`p-3 rounded-xl transition-colors ${
            isRecording
              ? 'bg-red-500/20 text-red-400 animate-pulse'
              : 'bg-orion-accent/10 text-orion-accent hover:bg-orion-accent/20'
          }`}
          whileTap={{ scale: 0.95 }}
        >
          <AnimatePresence mode="wait">
            {isRecording ? (
              <motion.svg
                key="stop"
                initial={{ scale: 0 }}
                animate={{ scale: 1 }}
                exit={{ scale: 0 }}
                className="w-5 h-5"
                fill="currentColor"
                viewBox="0 0 24 24"
              >
                <rect x="6" y="6" width="12" height="12" rx="2" />
              </motion.svg>
            ) : (
              <motion.svg
                key="mic"
                initial={{ scale: 0 }}
                animate={{ scale: 1 }}
                exit={{ scale: 0 }}
                className="w-5 h-5"
                fill="currentColor"
                viewBox="0 0 24 24"
              >
                <path d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3z" />
                <path d="M17 11c0 2.76-2.24 5-5 5s-5-2.24-5-5H5c0 3.53 2.61 6.43 6 6.92V21h2v-3.08c3.39-.49 6-3.39 6-6.92h-2z" />
              </motion.svg>
            )}
          </AnimatePresence>
        </motion.button>

        {/* Send Button */}
        <motion.button
          onClick={handleSubmit}
          disabled={disabled || !text.trim() || isRecording}
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
        className="mt-2 text-center text-xs text-orion-textDim"
        animate={{ opacity: state === 'thinking' ? [0.5, 1, 0.5] : 0.5 }}
        transition={{ duration: 1.5, repeat: Infinity }}
      >
        {state === 'listening' && 'Écoute...'}
        {state === 'thinking' && 'ORION réfléchit...'}
        {state === 'responding' && 'Réponse en cours...'}
      </motion.div>
    </div>
  );
};
