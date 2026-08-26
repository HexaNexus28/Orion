// components/ui/HoloCards.tsx
// Cartes holographiques du HUD — flottent au-dessus du canvas 3D, déplaçables.
//
// AVANT : parseHoloCards() fabriquait ces cartes par EXPRESSION RÉGULIÈRE sur la prose de la
// réponse. Deux motifs, dont un mort — le bloc ```data n'était demandé au modèle NULLE PART, donc
// jamais émis. Le seul actif attrapait « **Libellé**: 42 » au vol : toute statistique en gras
// devenait une carte, par accident, et rien n'apparaissait quand ça comptait.
//
// MAINTENANT : la carte est produite par l'OUTIL qui a réellement fait le travail
// (ITool.BuildCard), voyage typée jusqu'ici, et se range par identifiant stable.
import { useState } from 'react';
import { motion, useDragControls } from 'framer-motion';
import type { HudCard, HudCardState } from '../../types/dto/agentDto';

/**
 * La gravité décide de la couleur, ici et seulement ici.
 *
 * Le backend envoie « warn », jamais « #f59e0b » : sinon changer le thème obligerait à modifier
 * des outils métier côté serveur.
 */
const ACCENT: Record<HudCardState, string> = {
  neutral: '#22d3ee',
  ok: '#22c55e',
  warn: '#f59e0b',
  critical: '#ef4444',
};

interface HoloCardItemProps {
  card: HudCard;
  index: number;
  onClose: (id: string) => void;
}

const HoloCardItem: React.FC<HoloCardItemProps> = ({ card, index, onClose }) => {
  const dragControls = useDragControls();
  const accent = ACCENT[card.state] ?? ACCENT.neutral;

  return (
    <motion.div
      drag
      dragControls={dragControls}
      dragMomentum={false}
      dragElastic={0}
      className="absolute select-none cursor-grab active:cursor-grabbing"
      style={{
        top: `${12 + index * 14}%`,
        left: index % 2 === 0 ? '4%' : 'auto',
        right: index % 2 === 1 ? '4%' : 'auto',
        zIndex: 20 + index,
        width: card.items?.length ? 210 : 170,
      }}
      initial={{ opacity: 0, scale: 0.8, y: -10 }}
      animate={{ opacity: 1, scale: 1, y: 0 }}
      exit={{ opacity: 0, scale: 0.7, y: 10 }}
      transition={{ delay: index * 0.08, duration: 0.35, ease: 'easeOut' }}
      whileDrag={{ scale: 1.04, zIndex: 50 }}
    >
      <div
        className="absolute -inset-1 rounded-2xl blur-sm opacity-20"
        style={{ background: `radial-gradient(circle, ${accent}, transparent 70%)` }}
      />

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
        <div className="h-0.5 w-full" style={{ background: `linear-gradient(90deg, transparent, ${accent}, transparent)` }} />

        <div className="px-4 py-3">
          <button
            className="absolute top-2 right-2 w-4 h-4 rounded-full flex items-center justify-center text-[9px] opacity-40 hover:opacity-80 transition-opacity"
            style={{ color: accent, border: `1px solid ${accent}50` }}
            onClick={() => onClose(card.id)}
          >
            ×
          </button>

          <p className="text-[9px] uppercase tracking-[0.2em] font-medium mb-1" style={{ color: `${accent}99` }}>
            {card.label}
          </p>

          {card.value && (
            <p className="text-lg font-bold leading-tight" style={{ color: accent, textShadow: `0 0 10px ${accent}60` }}>
              {card.value}
              {card.unit && <span className="text-[10px] ml-1 font-normal opacity-60">{card.unit}</span>}
            </p>
          )}

          {/* Lignes de détail. Une source porte une URL et devient ouvrable ; les autres restent
              du texte — c'est la donnée qui décide, pas le type de carte. */}
          {card.items && card.items.length > 0 && (
            <ul className="mt-2 space-y-1">
              {card.items.map((item, k) => (
                <li key={k} className="text-[10px] leading-snug flex gap-1.5" style={{ color: `${accent}cc` }}>
                  <span className="opacity-40">›</span>
                  {item.url ? (
                    <a
                      href={item.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="underline decoration-dotted underline-offset-2 hover:opacity-100 opacity-80 truncate"
                    >
                      {item.label}
                    </a>
                  ) : (
                    <span className="truncate opacity-80">{item.label}</span>
                  )}
                  {item.value && <span className="ml-auto shrink-0 opacity-60">{item.value}</span>}
                </li>
              ))}
            </ul>
          )}
        </div>

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
  cards: HudCard[];
}

export const HoloCards: React.FC<HoloCardsProps> = ({ cards }) => {
  // Masquage LOCAL et non suppression du magasin : une carte permanente rafraîchie par le
  // serveur reviendrait aussitôt si on la retirait de la source. Ici, fermer veut dire
  // « ne me la montre plus », et ça tient.
  const [dismissed, setDismissed] = useState<Set<string>>(new Set());

  const visible = cards.filter(c => !dismissed.has(c.id)).slice(0, 5);
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