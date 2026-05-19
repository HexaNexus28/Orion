import { motion } from 'framer-motion';

export interface DataCard {
  title: string;
  value: string | number;
  subtitle?: string;
  color?: string;
  icon?: string;
}

interface DataCardsProps {
  cards: DataCard[];
}

/**
 * DataCards — Hologram-style 2D data cards
 * Displayed alongside ResponseText when ORION returns structured data
 * Glassmorphism + glow effect for the holographic feel
 */
export const DataCards: React.FC<DataCardsProps> = ({ cards }) => {
  if (!cards.length) return null;

  return (
    <div className="flex flex-wrap gap-3 justify-center">
      {cards.map((card, i) => (
        <motion.div
          key={`${card.title}-${i}`}
          className="relative min-w-[140px] max-w-[200px] px-4 py-3 rounded-xl
            bg-gradient-to-br from-purple-900/40 to-indigo-900/30
            border border-purple-500/30 backdrop-blur-md
            shadow-[0_0_15px_rgba(139,92,246,0.15)]"
          initial={{ opacity: 0, y: 20, scale: 0.9 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          transition={{ delay: i * 0.1, duration: 0.4, ease: 'easeOut' }}
        >
          {/* Glow accent */}
          <div
            className="absolute -inset-px rounded-xl opacity-20 blur-sm"
            style={{ background: card.color || '#8b5cf6' }}
          />

          {/* Icon */}
          {card.icon && (
            <span className="text-lg mb-1 block">{card.icon}</span>
          )}

          {/* Title */}
          <p className="text-[10px] uppercase tracking-widest text-purple-300/80 font-medium">
            {card.title}
          </p>

          {/* Value */}
          <p
            className="text-xl font-bold mt-0.5 leading-tight"
            style={{ color: card.color || '#c4b5fd' }}
          >
            {card.value}
          </p>

          {/* Subtitle */}
          {card.subtitle && (
            <p className="text-[11px] text-gray-400 mt-1">{card.subtitle}</p>
          )}
        </motion.div>
      ))}
    </div>
  );
};

/**
 * Parse structured data from ORION response text
 * Detects multiple patterns:
 * - ```data JSON blocks
 * - Markdown tables
 * - Bold labels with numeric values
 * - List items with numbers
 * - Currency/percent patterns
 */
export function parseDataCards(text: string): DataCard[] {
  const cards: DataCard[] = [];

  // Pattern 1: Detect ```data JSON blocks
  const dataBlockRegex = /```data\s*\n([\s\S]*?)```/g;
  let match;
  while ((match = dataBlockRegex.exec(text)) !== null) {
    try {
      const parsed = JSON.parse(match[1]);
      if (Array.isArray(parsed)) {
        cards.push(...parsed.map((item: Record<string, unknown>) => ({
          title: String(item.title || item.label || ''),
          value: String(item.value ?? item.count ?? ''),
          subtitle: item.subtitle ? String(item.subtitle) : undefined,
          icon: item.icon ? String(item.icon) : undefined,
          color: item.color ? String(item.color) : undefined,
        })));
      }
    } catch {
      // Not valid JSON, skip
    }
  }

  // Pattern 2: Markdown table rows | Label | Value |
  const tableRowRegex = /\|\s*([^|]+)\s*\|\s*([^|]+)\s*\|/g;
  let tableMatch;
  while ((tableMatch = tableRowRegex.exec(text)) !== null) {
    const label = tableMatch[1].trim();
    const value = tableMatch[2].trim();
    // Skip header rows
    if (!label.match(/^(label|key|title|name|stat|nombre)$/i) &&
        value.length < 30 &&
        /\d/.test(value)) {
      cards.push({ title: label, value });
    }
  }

  // Pattern 3: Bullet list with numbers: "- 42 utilisateurs" or "• MRR: €5,000"
  const bulletStatRegex = /^[-•\*]\s*(?:([^:]+):\s*)?([\d\s.,€$%KkM]+(?:\s*\w+)?)/gm;
  let bulletMatch;
  while ((bulletMatch = bulletStatRegex.exec(text)) !== null) {
    const label = bulletMatch[1]?.trim() || '';
    const value = bulletMatch[2].trim();
    if (value.length > 0 && value.length < 25 && /\d/.test(value)) {
      cards.push({
        title: label || detectStatType(value),
        value,
        icon: detectIcon(value)
      });
    }
  }

  // Pattern 4: Bold labels with numeric values "**Label**: Value" or "**Label** — Value"
  const boldStatRegex = /\*\*([^*]{2,20})\*\*\s*[:：\-—]\s*([\d\s.,€$%+\-]+(?:\s*\w{0,10}))/g;
  let boldMatch;
  while ((boldMatch = boldStatRegex.exec(text)) !== null) {
    const title = boldMatch[1].trim();
    const value = boldMatch[2].trim();
    if (value.length < 30 && /\d/.test(value)) {
      cards.push({
        title,
        value,
        icon: detectIcon(value),
        color: detectColor(value)
      });
    }
  }

  // Pattern 5: Inline stats like "40 utilisateurs actifs" or "€49/mois"
  const inlineStatRegex = /([\d\s.,]+(?:K|k|M|m|€|$|%)?)\s*(utilisateurs?|clients?|revenus?|MRR|ARR|chiffre|pourcent|taux|visites?|pages?|ventes?|commandes?|heures?|jours?|mois?)/gi;
  let inlineMatch;
  while ((inlineMatch = inlineStatRegex.exec(text)) !== null) {
    const value = inlineMatch[1].trim();
    const unit = inlineMatch[2];
    const existing = cards.find(c => c.title.toLowerCase().includes(unit.toLowerCase()));
    if (!existing && value.length < 15) {
      cards.push({
        title: capitalizeFirst(unit),
        value,
        icon: detectIcon(value + unit)
      });
    }
  }

  // Deduplicate by title (case-insensitive)
  const seen = new Set<string>();
  const unique = cards.filter(c => {
    const key = c.title.toLowerCase();
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });

  return unique.slice(0, 6); // Max 6 cards
}

// Helpers
function detectStatType(value: string): string {
  if (/€|\$|EUR|USD/.test(value)) return 'Montant';
  if (/%|pourcent/.test(value.toLowerCase())) return 'Pourcentage';
  if (/utilisateur|client|user/.test(value.toLowerCase())) return 'Utilisateurs';
  if (/vente|commande|sale/.test(value.toLowerCase())) return 'Ventes';
  if (/heure|hour|min|minute/.test(value.toLowerCase())) return 'Temps';
  return 'Stat';
}

function detectIcon(value: string): string | undefined {
  if (/€|\$/.test(value)) return '💰';
  if (/user|person|utilisateur|client/.test(value.toLowerCase())) return '👥';
  if (/%/.test(value)) return '📊';
  if (/time|hour|heure|minute/.test(value.toLowerCase())) return '⏱️';
  if (/sale|vente|order|commande/.test(value.toLowerCase())) return '🛒';
  if (/up|growth|croissance|↑/.test(value.toLowerCase())) return '📈';
  if (/down|decline|↓/.test(value.toLowerCase())) return '📉';
  return undefined;
}

function detectColor(value: string): string | undefined {
  if (/up|growth|croissance|↑|positive/.test(value.toLowerCase())) return '#22c55e'; // green
  if (/down|decline|↓|negative/.test(value.toLowerCase())) return '#ef4444'; // red
  if (/€|\$/.test(value)) return '#f59e0b'; // amber
  if (/%/.test(value)) return '#8b5cf6'; // purple
  return undefined;
}

function capitalizeFirst(str: string): string {
  return str.charAt(0).toUpperCase() + str.slice(1).toLowerCase();
}

export default DataCards;
