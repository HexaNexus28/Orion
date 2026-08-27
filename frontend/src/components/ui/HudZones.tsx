import { useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import type { HudCard, HudCardState } from '../../types/dto/agentDto';
import { useHudCards } from '../../context/HudCardsContext';

/**
 * Les zones du HUD.
 *
 * CE QUI CHANGE. Avant, toutes les cartes flottaient au même endroit, déplaçables, et
 * disparaissaient ensemble au message suivant. Un panneau permanent et une notification
 * occupaient le même espace — donc rien n'était permanent, et l'écran était vide entre deux
 * phrases.
 *
 * Ici l'écran a des zones fixes, comme un tableau de bord :
 *
 *   ┌──────────────┬────────────────────┬──────────────┐
 *   │  CONTEXTE    │       ORION        │   ACTIVITÉ   │
 *   │  (permanent) │   orbe + réponse   │  (éphémère)  │
 *   └──────────────┴────────────────────┴──────────────┘
 *
 * Le centre reste à ORION : les colonnes n'interceptent pas les clics (`pointer-events-none`
 * sur le conteneur, réactivé carte par carte), sinon elles bloqueraient l'interaction avec la
 * scène 3D qui occupe tout l'écran.
 *
 * Sur téléphone les colonnes deviennent des bandeaux escamotables — l'espace n'y suffit pas pour
 * trois zones, et le centre doit rester lisible.
 */

/** La gravité décide de la couleur, ici et seulement ici. Le backend envoie « warn », jamais un code hexadécimal. */
const ACCENT: Record<HudCardState, string> = {
  neutral: '#22d3ee',
  ok: '#22c55e',
  warn: '#f59e0b',
  critical: '#ef4444',
};

const Card: React.FC<{ card: HudCard; onDismiss?: (id: string) => void }> = ({ card, onDismiss }) => {
  const accent = ACCENT[card.state] ?? ACCENT.neutral;

  return (
    <motion.div
      layout
      initial={{ opacity: 0, x: -8 }}
      animate={{ opacity: 1, x: 0 }}
      exit={{ opacity: 0, scale: 0.95 }}
      transition={{ duration: 0.25, ease: 'easeOut' }}
      className="pointer-events-auto rounded-lg overflow-hidden"
      style={{
        background: 'rgba(5, 5, 20, 0.78)',
        border: `1px solid ${accent}33`,
        boxShadow: `0 0 10px ${accent}12`,
        backdropFilter: 'blur(10px)',
      }}
    >
      <div className="h-px w-full" style={{ background: `linear-gradient(90deg, transparent, ${accent}, transparent)` }} />

      <div className="px-3 py-2">
        <div className="flex items-baseline justify-between gap-2">
          <p className="text-[9px] uppercase tracking-[0.18em]" style={{ color: `${accent}99` }}>
            {card.label}
          </p>
          {onDismiss && (
            <button
              className="text-[9px] opacity-30 hover:opacity-70 transition-opacity"
              style={{ color: accent }}
              onClick={() => onDismiss(card.id)}
              aria-label="Masquer"
            >
              ×
            </button>
          )}
        </div>

        {card.value && (
          <p className="mt-0.5 text-sm font-semibold leading-tight truncate" style={{ color: accent }}>
            {card.value}
            {card.unit && <span className="ml-1 text-[10px] font-normal opacity-55">{card.unit}</span>}
          </p>
        )}

        {card.items && card.items.length > 0 && (
          <ul className="mt-1.5 space-y-0.5">
            {card.items.slice(0, 6).map((item, k) => (
              <li key={k} className="flex gap-1.5 text-[10px] leading-snug" style={{ color: `${accent}bb` }}>
                <span className="opacity-35">›</span>
                {item.url ? (
                  <a
                    href={item.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="truncate underline decoration-dotted underline-offset-2 opacity-80 hover:opacity-100"
                  >
                    {item.label}
                  </a>
                ) : (
                  <span className="truncate opacity-80">{item.label}</span>
                )}
                {item.value && <span className="ml-auto shrink-0 opacity-55">{item.value}</span>}
              </li>
            ))}
          </ul>
        )}
      </div>
    </motion.div>
  );
};

const Column: React.FC<{
  title: string;
  cards: HudCard[];
  side: 'left' | 'right';
  onDismiss?: (id: string) => void;
}> = ({ title, cards, side, onDismiss }) => {
  if (cards.length === 0) return null;

  return (
    <div
      className={`pointer-events-none absolute top-16 bottom-24 z-20 hidden w-56 flex-col gap-2 overflow-y-auto md:flex ${
        side === 'left' ? 'left-4' : 'right-4'
      }`}
    >
      <p className="text-[8px] uppercase tracking-[0.3em] text-cyan-300/30">{title}</p>
      <AnimatePresence mode="popLayout">
        {cards.map(card => (
          <Card key={card.id} card={card} onDismiss={onDismiss} />
        ))}
      </AnimatePresence>
    </div>
  );
};

export const HudZones: React.FC = () => {
  const { pinned, transient } = useHudCards();

  // Masquage LOCAL, pas suppression du magasin : un widget permanent rafraîchi par le serveur
  // reviendrait aussitôt si on le retirait de la source. Ici « masquer » veut dire « ne me le
  // montre plus », et ça tient.
  const [hidden, setHidden] = useState<Set<string>>(new Set());
  const hide = (id: string) => setHidden(prev => new Set([...prev, id]));

  const visiblePinned = pinned.filter(c => !hidden.has(c.id));
  const visibleTransient = transient.filter(c => !hidden.has(c.id)).slice(0, 5);

  return (
    <>
      <Column title="Contexte" cards={visiblePinned} side="left" onDismiss={hide} />
      <Column title="Activité" cards={visibleTransient} side="right" onDismiss={hide} />

      {/* Téléphone : une seule bande en bas, les permanents d'abord — ils portent l'état, les
          éphémères ne sont qu'un écho de ce qui vient de se passer. */}
      {(visiblePinned.length > 0 || visibleTransient.length > 0) && (
        <div className="pointer-events-none absolute inset-x-3 bottom-20 z-20 flex gap-2 overflow-x-auto md:hidden">
          {[...visiblePinned, ...visibleTransient].slice(0, 4).map(card => (
            <div key={card.id} className="w-44 shrink-0">
              <Card card={card} onDismiss={hide} />
            </div>
          ))}
        </div>
      )}
    </>
  );
};
