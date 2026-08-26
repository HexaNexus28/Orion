import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import type { HudCard } from '../types/dto/agentDto';

/**
 * Magasin des cartes du HUD.
 *
 * POURQUOI UN CONTEXTE ET PAS L'ÉTAT DE useStream. L'état d'un tour de conversation est remis à
 * zéro à chaque message envoyé. Une carte permanente — l'état du poste, un dépôt suivi — y
 * disparaîtrait dès que tu écris quelque chose. Or le HUD doit être là EN PERMANENCE, comme la
 * fenêtre de statut d'un jeu : elle ne s'affiche pas parce qu'on a posé une question.
 *
 * DEUX SOURCES, un seul magasin :
 *   - le flux de chat        → cartes produites par les outils du tour en cours
 *   - le flux proactif (SSE) → cartes poussées par le serveur, indépendamment de toi
 *
 * L'identifiant STABLE est ce qui les réconcilie : une carte poussée remplace celle de même id,
 * d'où qu'elle vienne. Sans lui, les deux sources empileraient des doublons périmés.
 */
interface HudCardsValue {
  cards: HudCard[];
  /** Ajoute la carte, ou remplace celle qui porte le même identifiant. */
  upsertCard: (card: HudCard) => void;
  dismissCard: (id: string) => void;
  clearCards: () => void;
}

const HudCardsContext = createContext<HudCardsValue | null>(null);

export const HudCardsProvider = ({ children }: { children: ReactNode }) => {
  const [cards, setCards] = useState<HudCard[]>([]);

  const upsertCard = useCallback((card: HudCard) => {
    setCards(prev => {
      const i = prev.findIndex(c => c.id === card.id);
      if (i < 0) return [...prev, card];

      // Remplacement EN PLACE : la carte garde sa position à l'écran. La déplacer en fin de
      // liste à chaque rafraîchissement ferait sauter le HUD toutes les minutes.
      const suivant = [...prev];
      suivant[i] = card;
      return suivant;
    });
  }, []);

  const dismissCard = useCallback((id: string) => {
    setCards(prev => prev.filter(c => c.id !== id));
  }, []);

  const clearCards = useCallback(() => setCards([]), []);

  const value = useMemo(
    () => ({ cards, upsertCard, dismissCard, clearCards }),
    [cards, upsertCard, dismissCard, clearCards]
  );

  return <HudCardsContext.Provider value={value}>{children}</HudCardsContext.Provider>;
};

export const useHudCards = (): HudCardsValue => {
  const ctx = useContext(HudCardsContext);
  if (!ctx) throw new Error('useHudCards doit être utilisé dans un HudCardsProvider');
  return ctx;
};