import { useEffect, useRef, useState } from 'react';
import { motion } from 'framer-motion';
import type { ResponseTextProps } from '../../types';

export const ResponseText: React.FC<ResponseTextProps> = ({
  text,
  isStreaming = false,
  speed = 'normal'
}) => {
  const [displayText, setDisplayText] = useState('');
  const [isComplete, setIsComplete] = useState(false);
  // Indique si le texte courant a été affiché via le stream (pas besoin de typewriter)
  const wasStreamedRef = useRef(false);

  const speedMap = {
    slow: 50,
    normal: 30,
    fast: 15
  };

  useEffect(() => {
    if (isStreaming) {
      // Afficher les chunks au fur et à mesure — déjà "typé" par le stream
      wasStreamedRef.current = true;
      setDisplayText(text);
      setIsComplete(false);
      return;
    }

    if (wasStreamedRef.current) {
      // Le stream vient de se terminer : le texte est déjà affiché, pas de typewriter
      wasStreamedRef.current = false;
      setIsComplete(true);
      return;
    }

    // Texte posé d'un coup (non streamé) → typewriter
    wasStreamedRef.current = false;
    setIsComplete(false);
    setDisplayText('');

    let index = 0;
    const interval = setInterval(() => {
      if (index < text.length) {
        setDisplayText(text.slice(0, index + 1));
        index++;
      } else {
        setIsComplete(true);
        clearInterval(interval);
      }
    }, speedMap[speed]);

    return () => clearInterval(interval);
  }, [text, isStreaming, speed]);

  return (
    <motion.div
      className="max-w-2xl mx-auto p-6 rounded-lg bg-opacity-10 bg-cyan-900 border border-cyan-500/20"
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5 }}
    >
      <p className="text-orion-text font-mono text-lg leading-relaxed whitespace-pre-wrap">
        {displayText}
        {!isComplete && (
          <motion.span
            className="inline-block w-2 h-5 bg-orion-accent ml-1"
            animate={{ opacity: [1, 0] }}
            transition={{ duration: 0.5, repeat: Infinity }}
          />
        )}
      </p>
    </motion.div>
  );
};
