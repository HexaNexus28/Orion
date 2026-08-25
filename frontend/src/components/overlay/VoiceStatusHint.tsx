import type { VoiceStatusHintProps } from '../../types';

/**
 * La ligne d'état sous l'orbe.
 *
 * L'interface ne disait RIEN : quatre points de 1,5 px avec des infobulles, et aucune indication
 * qu'ORION écoute en permanence ni comment l'interpeller. Impossible de distinguer « il
 * m'entend » de « le micro est mort ». Ce composant répond à une seule question, tout le temps :
 * qu'est-ce qu'ORION est en train de faire, et que puis-je faire maintenant ?
 */
export const VoiceStatusHint = ({
  state,
  isListening,
  isSpeaking,
  micDenied,
  onRetryMic,
}: VoiceStatusHintProps) => {
  // Le micro refusé est le seul cas ACTIONNABLE : il passe devant tout le reste.
  if (micDenied) {
    return (
      <div className="absolute bottom-16 left-0 right-0 flex justify-center z-20 px-4">
        <button
          type="button"
          onClick={onRetryMic}
          className="rounded-full border border-red-400/40 bg-red-500/10 px-4 py-2 text-sm
                     text-red-200 backdrop-blur-md transition-colors hover:bg-red-500/20"
        >
          Micro non autorisé — clique pour réessayer
        </button>
      </div>
    );
  }

  const label = (() => {
    if (state === 'thinking') return 'Je réfléchis…';
    if (state === 'responding') return null; // la réponse s'affiche déjà, ne pas la doubler
    if (isSpeaking) return 'Je t’entends…';
    if (isListening) return 'Parle — je t’écoute';
    return 'Touche l’orbe pour écrire';
  })();

  if (!label) return null;

  return (
    <div className="absolute bottom-16 left-0 right-0 flex justify-center z-10 px-4 pointer-events-none">
      <span
        className={`text-sm tracking-wide transition-opacity duration-500 ${
          isSpeaking ? 'text-orion-accent/90' : 'text-orion-light/45'
        }`}
      >
        {label}
      </span>
    </div>
  );
};

export default VoiceStatusHint;
