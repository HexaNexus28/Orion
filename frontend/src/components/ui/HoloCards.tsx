// components/ui/HoloCards.tsx
// Draggable holographic info cards — floating over the 3D canvas
import { useState, useRef } from 'react';
import { motion, useDragControls } from 'framer-motion';

export interface HoloCard {
  id: string;
  title: string;
  value: string | number;
  subtitle?: string;
  color?: string;
  icon?: string;
}

interface HoloCardItemProps {
  card: HoloCard;
  index: number;
  onClose: (id: string) => void;
}

const HoloCardItem: React.FC<HoloCardItemProps> = ({ card, index, onClose }) => {
  const dragControls = useDragControls();
  const constraintRef = useRef<HTMLDivElement>(null);

  const accent = card.color ?? '#22d3ee';

  return (
    <motion.div
      drag
      dragControls={dragControls}
      dragMomentum={false}
      dragElastic={0}
      className="absolute select-none cursor-grab active:cursor-grabbing"
      style={{
        top: `${15 + index * 12}%`,
        left: index % 2 === 0 ? '5%' : 'auto',
        right: index % 2 === 1 ? '5%' : 'auto',
        zIndex: 20 + index,
        width: 160,
      }}
      initial={{ opacity: 0, scale: 0.8, y: -10 }}
      animate={{ opacity: 1, scale: 1, y: 0 }}
      exit={{ opacity: 0, scale: 0.7, y: 10 }}
      transition={{ delay: index * 0.08, duration: 0.35, ease: 'easeOut' }}
      whileDrag={{ scale: 1.04, zIndex: 50 }}
    >
      {/* Outer glow */}
      <div
        className="absolute -inset-1 rounded-2xl blur-sm opacity-20"
        style={{ background: `radial-gradient(circle, ${accent}, transparent 70%)` }}
      />

      {/* Card body */}
      <div
        className="relative rounded-xl overflow-hidden"
        style={{
          background: 'rgba(5, 5, 20, 0.82)',
          border: `1px solid ${accent}40`,
          boxShadow: `0 0 12px ${accent}18, inset 0 1px 0 ${accent}20`,
          backdropFilter: 'blur(12px)',
        }}
        onPointerDown={(e) => dragControls.start(e)}
      >
        {/* Top accent bar */}
        <div className="h-0.5 w-full" style={{ background: `linear-gradient(90deg, transparent, ${accent}, transparent)` }} />

        <div className="px-4 py-3">
          {/* Close button */}
          <button
            className="absolute top-2 right-2 w-4 h-4 rounded-full flex items-center justify-center text-[9px] opacity-40 hover:opacity-80 transition-opacity"
            style={{ color: accent, border: `1px solid ${accent}50` }}
            onClick={() => onClose(card.id)}
          >
            ×
          </button>

          {/* Icon */}
          {card.icon && <div className="text-lg mb-1">{card.icon}</div>}

          {/* Title */}
          <p className="text-[9px] uppercase tracking-[0.2em] font-medium mb-1" style={{ color: `${accent}99` }}>
            {card.title}
          </p>

          {/* Value */}
          <p className="text-lg font-bold leading-tight" style={{ color: accent, textShadow: `0 0 10px ${accent}60` }}>
            {card.value}
          </p>

          {/* Subtitle */}
          {card.subtitle && (
            <p className="text-[10px] mt-1 opacity-60" style={{ color: accent }}>
              {card.subtitle}
            </p>
          )}
        </div>

        {/* Bottom scan line animation */}
        <motion.div
          className="absolute bottom-0 left-0 right-0 h-px opacity-40"
          style={{ background: `linear-gradient(90deg, transparent, ${accent}, transparent)` }}
          animate={{ x: ['-100%', '100%'] }}
          transition={{ duration: 2.5, repeat: Infinity, ease: 'linear' }}
        />
      </div>
    </motion.div>
  );
};

interface HoloCardsProps {
  cards: HoloCard[];
}

export const HoloCards: React.FC<HoloCardsProps> = ({ cards }) => {
  const [dismissed, setDismissed] = useState<Set<string>>(new Set());

  const visible = cards.filter(c => !dismissed.has(c.id));
  if (!visible.length) return null;

  const handleClose = (id: string) => {
    setDismissed(prev => new Set([...prev, id]));
  };

  return (
    <div className="absolute inset-0 z-20 pointer-events-none">
      {visible.map((card, i) => (
        <div key={card.id} className="pointer-events-auto">
          <HoloCardItem card={card} index={i} onClose={handleClose} />
        </div>
      ))}
    </div>
  );
};

// ── Parser: extract structured data from ORION response ─────────────────────
export function parseHoloCards(text: string): HoloCard[] {
  const cards: HoloCard[] = [];

  // Pattern 1: ```data JSON block
  const dataBlock = /```data\s*\n([\s\S]*?)```/g;
  let m: RegExpExecArray | null;
  while ((m = dataBlock.exec(text)) !== null) {
    try {
      const parsed = JSON.parse(m[1]);
      if (Array.isArray(parsed)) {
        parsed.forEach((item: Record<string, unknown>, i: number) => {
          cards.push({
            id: `data-${i}`,
            title: String(item.title ?? item.label ?? ''),
            value: String(item.value ?? item.count ?? ''),
            subtitle: item.subtitle ? String(item.subtitle) : undefined,
            icon: item.icon ? String(item.icon) : undefined,
            color: item.color ? String(item.color) : undefined,
          });
        });
      }
    } catch { /* skip invalid JSON */ }
  }

  // Pattern 2: Bold stat "**Label**: value" with a number
  const boldStat = /\*\*([^*]{2,20})\*\*\s*[:：\-—]\s*([\d\s.,€$%+\-]+(?:\s*\w{0,8}))/g;
  let bm: RegExpExecArray | null;
  while ((bm = boldStat.exec(text)) !== null) {
    const title = bm[1].trim();
    const value = bm[2].trim();
    if (value.length < 25 && /\d/.test(value) && !cards.find(c => c.title.toLowerCase() === title.toLowerCase())) {
      cards.push({
        id: `bold-${cards.length}`,
        title,
        value,
        color: /€|\$/.test(value) ? '#f59e0b' : /%/.test(value) ? '#8b5cf6' : '#22d3ee',
        icon: /€|\$/.test(value) ? '💰' : /%/.test(value) ? '📊' : undefined,
      });
    }
  }

  // Deduplicate by title
  const seen = new Set<string>();
  return cards.filter(c => {
    const key = c.title.toLowerCase();
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  }).slice(0, 5);
}
