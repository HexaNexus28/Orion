import { useEffect, useRef, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import ReactMarkdown from 'react-markdown';
import remarkBreaks from 'remark-breaks';

// Fix incomplete markdown during streaming
function sanitizeStreamingMarkdown(text: string): string {
  const boldCount = (text.match(/\*\*/g) || []).length;
  if (boldCount % 2 !== 0) text += '**';
  const codeCount = (text.match(/`/g) || []).length;
  if (codeCount % 2 !== 0) text += '`';
  return text;
}

interface HologramResponseProps {
  text: string;
  isStreaming?: boolean;
}

export const HologramResponse: React.FC<HologramResponseProps> = ({
  text,
  isStreaming = false,
}) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const [scanlineOffset, setScanlineOffset] = useState(0);

  // Animated scanline
  useEffect(() => {
    if (!isStreaming) return;
    const interval = setInterval(() => {
      setScanlineOffset(prev => (prev + 1) % 200);
    }, 50);
    return () => clearInterval(interval);
  }, [isStreaming]);

  // Auto-scroll during streaming
  useEffect(() => {
    if (containerRef.current && isStreaming) {
      containerRef.current.scrollTop = containerRef.current.scrollHeight;
    }
  }, [text, isStreaming]);

  const displayText = isStreaming ? sanitizeStreamingMarkdown(text) : text;

  return (
    <AnimatePresence>
      {text && (
        <motion.div
          initial={{ opacity: 0, scale: 0.95, y: 20 }}
          animate={{ opacity: 1, scale: 1, y: 0 }}
          exit={{ opacity: 0, scale: 0.95, y: -10 }}
          transition={{ duration: 0.4, ease: 'easeOut' }}
          className="relative w-full max-w-2xl mx-auto"
        >
          {/* Outer glow */}
          <div className="absolute -inset-[1px] rounded-xl bg-gradient-to-r from-cyan-500/30 via-violet-500/20 to-cyan-500/30 blur-sm" />

          {/* Main container */}
          <div className="relative rounded-xl overflow-hidden"
            style={{
              background: 'linear-gradient(135deg, rgba(6,12,26,0.85) 0%, rgba(15,23,42,0.9) 50%, rgba(6,12,26,0.85) 100%)',
              backdropFilter: 'blur(20px)',
              border: '1px solid rgba(34,211,238,0.15)',
              boxShadow: `
                0 0 20px rgba(34,211,238,0.08),
                inset 0 1px 0 rgba(34,211,238,0.1),
                inset 0 -1px 0 rgba(139,92,246,0.05)
              `,
            }}
          >
            {/* Top accent bar */}
            <div className="h-[2px] w-full bg-gradient-to-r from-transparent via-cyan-400/60 to-transparent" />

            {/* Header with ORION indicator */}
            <div className="flex items-center gap-2 px-4 pt-3 pb-1">
              <div className="relative">
                <div className="w-2 h-2 rounded-full bg-cyan-400 animate-pulse" />
                <div className="absolute inset-0 w-2 h-2 rounded-full bg-cyan-400 animate-ping opacity-30" />
              </div>
              <span className="text-[10px] font-mono uppercase tracking-[0.2em] text-cyan-400/60">
                ORION {isStreaming ? '● streaming' : ''}
              </span>

              {/* Streaming indicator */}
              {isStreaming && (
                <motion.div
                  className="ml-auto flex gap-1"
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                >
                  {[0, 1, 2].map(i => (
                    <motion.div
                      key={i}
                      className="w-1 h-3 bg-cyan-400/60 rounded-full"
                      animate={{ scaleY: [0.3, 1, 0.3] }}
                      transition={{
                        duration: 0.8,
                        repeat: Infinity,
                        delay: i * 0.15,
                      }}
                    />
                  ))}
                </motion.div>
              )}
            </div>

            {/* Content area with holographic effects */}
            <div className="relative px-5 py-3">
              {/* Scanline overlay during streaming */}
              {isStreaming && (
                <div
                  className="absolute inset-0 pointer-events-none opacity-[0.03]"
                  style={{
                    backgroundImage: `repeating-linear-gradient(
                      0deg,
                      transparent,
                      transparent 2px,
                      rgba(34,211,238,0.5) 2px,
                      rgba(34,211,238,0.5) 3px
                    )`,
                    backgroundPositionY: `${scanlineOffset}px`,
                  }}
                />
              )}

              {/* Scrollable text content */}
              <div
                ref={containerRef}
                className="max-h-[50vh] overflow-y-auto scrollbar-thin scrollbar-thumb-cyan-900/50 scrollbar-track-transparent"
                style={{
                  maskImage: 'linear-gradient(to bottom, black 85%, transparent 100%)',
                  WebkitMaskImage: 'linear-gradient(to bottom, black 85%, transparent 100%)',
                }}
              >
                <div className="
                  text-gray-200/90 text-[15px] leading-relaxed font-light
                  prose prose-invert prose-sm max-w-none
                  prose-headings:text-cyan-300 prose-headings:font-medium prose-headings:text-sm prose-headings:mt-3 prose-headings:mb-1
                  prose-strong:text-cyan-200 prose-strong:font-medium
                  prose-ul:my-1 prose-ul:pl-4 prose-li:my-0.5 prose-li:marker:text-cyan-500/50
                  prose-p:my-1.5
                  prose-code:text-violet-300 prose-code:bg-violet-500/10 prose-code:px-1.5 prose-code:py-0.5 prose-code:rounded prose-code:text-xs
                ">
                  <ReactMarkdown remarkPlugins={[remarkBreaks]}>
                    {displayText}
                  </ReactMarkdown>
                </div>
              </div>

              {/* Typing cursor */}
              {isStreaming && (
                <motion.span
                  className="inline-block w-[2px] h-4 bg-cyan-400 ml-0.5 -mb-0.5"
                  animate={{ opacity: [1, 0] }}
                  transition={{ duration: 0.5, repeat: Infinity }}
                />
              )}
            </div>

            {/* Bottom accent bar */}
            <div className="h-[1px] w-full bg-gradient-to-r from-transparent via-violet-500/30 to-transparent" />

            {/* Bottom corner decorations */}
            <div className="flex justify-between px-4 py-1.5">
              <div className="flex gap-2">
                <div className="w-8 h-[1px] bg-cyan-500/20 self-center" />
                <div className="w-3 h-[1px] bg-cyan-500/10 self-center" />
              </div>
              <div className="flex gap-2">
                <div className="w-3 h-[1px] bg-violet-500/10 self-center" />
                <div className="w-8 h-[1px] bg-violet-500/20 self-center" />
              </div>
            </div>
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  );
};

export default HologramResponse;
