import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import type { DeferredQueueBadgeProps } from '../../props/DeferredQueueBadge.props';


/**
 * La seule trace visible de la file, et elle n'apparaît QUE s'il y a quelque chose dedans.
 *
 * Une file invisible est une promesse qu'on ne peut plus reprendre ; une file toujours
 * affichée devient du décor qu'on cesse de lire. Elle se montre quand elle a une raison.
 */
export const DeferredQueueBadge: React.FC<DeferredQueueBadgeProps> = ({ enAttente, aConfirmer, onOpen }) => (
  <AnimatePresence>
    {enAttente > 0 && (
      <motion.button
        onClick={onOpen}
        className={`fixed top-4 right-4 z-20 flex items-center gap-2 px-3 py-1.5 rounded-full
          border backdrop-blur-sm text-xs
          ${aConfirmer > 0
            ? 'bg-orion-accent/20 border-orion-accent/50 text-orion-text'
            : 'bg-orion-dark/60 border-orion-accent/20 text-orion-textDim hover:text-orion-text'}`}
        initial={{ opacity: 0, y: -8 }}
        animate={{ opacity: 1, y: 0 }}
        exit={{ opacity: 0, y: -8 }}
      >
        <span
          className={`w-1.5 h-1.5 rounded-full ${aConfirmer > 0 ? 'bg-orion-accent animate-pulse' : 'bg-orion-accent/40'}`}
        />
        {aConfirmer > 0
          ? `${aConfirmer} action${aConfirmer > 1 ? 's' : ''} attend${aConfirmer > 1 ? 'ent' : ''} ton feu vert`
          : `${enAttente} en attente de ton PC`}
      </motion.button>
    )}
  </AnimatePresence>
);
