// components/response/OrionResponse.tsx
// HTML overlay — response text below the entity, outside the Canvas
import { useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';

interface OrionResponseProps {
  text: string;
  isStreaming: boolean;
}

export const OrionResponse: React.FC<OrionResponseProps> = ({ text, isStreaming }) => {
  const scrollRef = useRef<HTMLDivElement>(null);

  // Auto-scroll to bottom during streaming
  useEffect(() => {
    if (isStreaming && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [text, isStreaming]);

  if (!text) return null;

  return (
    <AnimatePresence>
      <motion.div
        key="response"
        className="absolute left-4 right-4 bottom-20 z-10 pointer-events-none"
        style={{ top: '55%' }}
        initial={{ opacity: 0, y: 10 }}
        animate={{ opacity: 1, y: 0 }}
        exit={{ opacity: 0, y: 5 }}
        transition={{ duration: 0.3 }}
      >
        <div
          ref={scrollRef}
          className="max-h-[38vh] overflow-y-auto scrollbar-none"
          style={{ maskImage: 'linear-gradient(to bottom, transparent 0%, black 8%, black 85%, transparent 100%)' }}
        >
          <p
            className="text-center text-sm leading-relaxed px-4 pb-6"
            style={{
              color: 'rgba(203, 213, 225, 0.92)',
              fontFamily: '"Inter", system-ui, sans-serif',
              letterSpacing: '0.015em',
              textShadow: '0 0 20px rgba(34, 211, 238, 0.15)',
            }}
          >
            {/* Render markdown-lite: bold */}
            {renderText(text)}
            {isStreaming && (
              <span
                className="inline-block w-0.5 h-3.5 bg-cyan-400 ml-0.5 align-middle"
                style={{ animation: 'pulse 0.8s ease-in-out infinite' }}
              />
            )}
          </p>
        </div>
      </motion.div>
    </AnimatePresence>
  );
};

// Minimal markdown renderer — bold + newlines only
function renderText(text: string): React.ReactNode[] {
  const lines = text.split('\n');
  const nodes: React.ReactNode[] = [];

  lines.forEach((line, li) => {
    if (li > 0) nodes.push(<br key={`br-${li}`} />);

    // Bold: **text**
    const parts = line.split(/(\*\*[^*]+\*\*)/g);
    parts.forEach((part, pi) => {
      if (part.startsWith('**') && part.endsWith('**')) {
        nodes.push(
          <strong key={`${li}-${pi}`} style={{ color: 'rgba(167, 139, 250, 0.95)' }}>
            {part.slice(2, -2)}
          </strong>
        );
      } else {
        nodes.push(<span key={`${li}-${pi}`}>{part}</span>);
      }
    });
  });

  return nodes;
}
