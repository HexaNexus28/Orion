import { useRef, useCallback, useState, useEffect } from 'react';
import { MicVAD } from '@ricky0123/vad-web';

interface UseVADOptions {
  onSpeechStart?: () => void;
  onSpeechEnd?: (audio: Float32Array) => void;
  /**
   * La prise est terminée et disponible. AUCUNE charge utile : l'audio part par
   * `onAudioChunk`, et l'appelant n'a besoin ici que du FRONT qui déclenche le tour.
   *
   * Avant, ce rappel rendait un Blob WAV — encodé échantillon par échantillon en JavaScript, à
   * chaque prise — dont l'appelant ne lisait jamais le contenu : il ne s'en servait que comme
   * booléen « audio prêt ». On encodait donc toute l'énonciation pour la jeter.
   */
  onSpeechCaptured?: () => void;
  onAudioChunk?: (pcm16: Int16Array) => void;
  onAmplitude?: (amplitude: number) => void;
  onError?: (error: string) => void;
}

/**
 * useVAD — détection de parole par MODÈLE (Silero v5), plus par volume.
 *
 * CE QUI A CHANGÉ. La version précédente comparait l'énergie RMS d'un bloc à un seuil fixe de
 * 0,015. Un seuil d'énergie ne distingue pas une voix d'un bruit : une porte, la télévision, une
 * conversation à côté déclenchaient un tour complet — transcription, modèle, réponse vocale —
 * pour du vide. Et le réglage était intenable : trop bas il partait sur tout, trop haut il ratait
 * une phrase prononcée doucement. Aucune valeur ne marche pour les deux.
 *
 * Silero répond à une autre question : « est-ce de la PAROLE HUMAINE ? », avec une probabilité par
 * tranche de 30 ms. Le volume n'entre plus en compte — on peut chuchoter, et la télévision ne
 * déclenche plus rien.
 *
 * Le modèle était DÉJÀ dans le dépôt : public/vad/silero_vad_v5.onnx et son moteur ONNX, 81 Mo
 * embarqués dans chaque image depuis des mois, jamais appelés. Le code chargeait à la place un
 * détecteur d'énergie écrit à la main. Ici on branche ce qui existait.
 *
 * Le contrat du hook est INCHANGÉ : App.tsx ne bouge pas.
 */

// Servis depuis public/vad : aucun téléchargement au premier usage, aucune dépendance à un CDN,
// et le service worker les précharge déjà.
const ASSET_PATH = '/vad/';

/**
 * Probabilité minimale pour déclarer « c'est de la parole ».
 *
 * 0,5 est la valeur de référence de Silero. Contrairement à un seuil d'énergie, elle ne dépend NI
 * du micro, NI de la distance, NI du volume — seulement de la confiance du modèle. C'est ce qui la
 * rend transposable d'un appareil à l'autre sans réglage.
 */
const POSITIVE_SPEECH_THRESHOLD = 0.5;
const NEGATIVE_SPEECH_THRESHOLD = 0.35;

// Silence toléré à l'intérieur d'une phrase avant de clore la prise.
//
// C'EST DU TEMPS MORT PUR : tu as fini de parler, et il ne se passe rien pendant toute cette
// durée. Elle s'ajoute telle quelle à la latence perçue, AVANT même la transcription, le
// modèle et la synthèse.
//
// 24 tranches (≈770 ms) était trop généreux — près d'une seconde d'attente à chaque phrase.
// 14 tranches ≈ 450 ms : on garde de quoi respirer au milieu d'une phrase, on rend 320 ms.
// Descendre plus bas coupe la parole de quelqu'un qui cherche son mot, ce qui est bien pire
// qu'attendre.
const REDEMPTION_FRAMES = 14;

// En dessous, c'est un bruit bref classé parole par erreur — ignoré au lieu de lancer un tour
// complet. 9 tranches ≈ 290 ms.
const MIN_SPEECH_FRAMES = 9;

// Audio conservé AVANT que Silero ne déclare « c'est de la parole ». 8 tranches ≈ 256 ms.
//
// La bibliothèque n'en garde qu'UNE seule par défaut, soit 32 ms. Or Silero met typiquement une
// à trois tranches à franchir son seuil de confiance : l'attaque du premier mot était donc
// coupée. « Ouvre Notepad » arrivait amputé de sa première syllabe — et un modèle de
// transcription ne rend jamais du vide, il invente le mot le plus plausible. C'est la signature
// de « il m'entend mais transcrit autre chose ».
//
// Ce pré-roll ne coûte rien : il n'allonge ni l'attente ni le tour, il ajoute seulement un
// quart de seconde d'audio déjà capturé au début de la prise.
const PRE_SPEECH_PAD_FRAMES = 8;

// Silero impose 512 échantillons par tranche à 16 kHz — d'où les ≈32 ms qui servent d'unité
// à tous les comptes ci-dessus.
//
// CONTRAT AVEC LE SERVEUR : MicVAD rééchantillonne lui-même à 16 kHz, et
// `VoiceWebSocketHandler.EncodePcmToWav` suppose 16 kHz EN DUR. Les deux moitiés ne se parlent
// pas — le WebSocket transporte du PCM brut, sans en-tête pour porter le taux. Changer l'un
// sans l'autre ne lève aucune erreur : la transcription devient simplement fausse.

export const useVAD = (options: UseVADOptions = {}) => {
  const [isSpeaking, setIsSpeaking] = useState(false);
  const [isListening, setIsListening] = useState(false);

  const vadRef = useRef<MicVAD | null>(null);
  const listeningRef = useRef(false);
  const startingRef = useRef(false);

  // Rappels dans une ref : les recréer changerait `start` à chaque rendu, et MicVAD serait détruit
  // puis reconstruit — avec un nouveau getUserMedia à chaque fois.
  const cbRef = useRef(options);
  cbRef.current = options;

  const start = useCallback(async () => {
    if (listeningRef.current || startingRef.current) return;
    startingRef.current = true;

    try {
      console.log('[VAD] Démarrage — modèle Silero v5');

      const vad = await MicVAD.new({
        model: 'v5',
        baseAssetPath: ASSET_PATH,
        onnxWASMBasePath: ASSET_PATH,

        positiveSpeechThreshold: POSITIVE_SPEECH_THRESHOLD,
        negativeSpeechThreshold: NEGATIVE_SPEECH_THRESHOLD,
        redemptionFrames: REDEMPTION_FRAMES,
        minSpeechFrames: MIN_SPEECH_FRAMES,
        preSpeechPadFrames: PRE_SPEECH_PAD_FRAMES,

        onSpeechStart: () => {
          setIsSpeaking(true);
          console.log('[VAD] Parole détectée');
          cbRef.current.onSpeechStart?.();
        },

        onSpeechEnd: (audio: Float32Array) => {
          setIsSpeaking(false);
          console.log('[VAD] Fin de prise —', audio.length, 'échantillons');

          // La prise complète part en une fois. Le découpage en continu ne servait que parce que
          // la transcription locale mettait cinq secondes : on recouvrait la parole et le calcul.
          // Avec Voxtral à 0,35 s ce recouvrement ne rapporte plus rien, et coûtait une machine à
          // états de plus.
          cbRef.current.onAudioChunk?.(floatTo16BitPCM(audio));
          cbRef.current.onSpeechEnd?.(audio);
          cbRef.current.onSpeechCaptured?.();
        },

        onVADMisfire: () => {
          setIsSpeaking(false);
          console.log('[VAD] Bruit bref ignoré');
        },

        onFrameProcessed: (probabilities) => {
          // On remonte la PROBABILITÉ DE PAROLE, plus le volume. L'indicateur dit enfin « ORION
          // pense que tu parles » au lieu de « il y a du son quelque part ».
          cbRef.current.onAmplitude?.(probabilities.isSpeech);
        },
      });

      vad.start();
      vadRef.current = vad;
      listeningRef.current = true;
      setIsListening(true);
      console.log('[VAD] Écoute active ✓');
    } catch (err) {
      // MicVAD appelle getUserMedia en interne : mêmes causes, mêmes distinctions. Les confondre
      // sous un seul message envoyait chercher au mauvais endroit.
      const errorName = err instanceof DOMException ? err.name : 'Erreur';

      // Un echec de CHARGEMENT DU MODELE ne doit pas se presenter comme un probleme de micro.
      // Le 2026-08-27, /vad/silero_vad_v5.onnx renvoyait 401 (type MIME inconnu cote serveur)
      // et l'interface parlait d'un micro absent : des heures passees a verifier des reglages
      // Edge et Windows pour une table de types MIME. Une panne doit dire ce qui a echoue.
      const brut = err instanceof Error ? err.message : '';
      if (/onnx|protobuf|session|model|graph/i.test(brut)) {
        const msgModele = 'Le detecteur de parole n a pas pu charger son modele — ce n est PAS un probleme de micro. Recharge la page ; si ca persiste, le fichier /vad/silero_vad_v5.onnx n est pas servi correctement.';
        console.error('[VAD] Chargement du modele impossible —', brut);
        cbRef.current.onError?.(msgModele);
        throw err;
      }
      const msg =
        errorName === 'NotAllowedError'
          ? "Micro bloqué par le navigateur — clique le cadenas à gauche de l'URL, mets Microphone sur « Autoriser », puis recharge."
          : errorName === 'NotFoundError'
            ? "Aucun microphone trouvé — vérifie qu'il est branché et activé dans Windows."
            : errorName === 'NotReadableError'
              ? "Micro occupé par une autre application — ferme Teams, Discord ou un onglet qui l'utilise."
              : errorName === 'SecurityError'
                ? 'Capture audio interdite dans ce contexte (connexion non sécurisée ?).'
                : err instanceof Error
                  ? err.message
                  : 'Accès microphone impossible';

      console.error('[VAD] Erreur démarrage —', errorName, err);
      cbRef.current.onError?.(msg);
      throw err; // l'appelant DOIT savoir que ça a échoué
    } finally {
      startingRef.current = false;
    }
  }, []);

  const pause = useCallback(() => {
    vadRef.current?.pause();
    listeningRef.current = false;
    setIsListening(false);
    setIsSpeaking(false);
  }, []);

  const resume = useCallback(() => {
    if (!vadRef.current) return;
    vadRef.current.start();
    listeningRef.current = true;
    setIsListening(true);
  }, []);

  const destroy = useCallback(() => {
    vadRef.current?.destroy();
    vadRef.current = null;
    listeningRef.current = false;
    setIsListening(false);
    setIsSpeaking(false);
  }, []);

  const reset = useCallback(() => setIsSpeaking(false), []);

  useEffect(() => () => { destroy(); }, [destroy]);

  return {
    isSpeaking,
    isListening,
    start,
    pause,
    resume,
    destroy,
    reset,

    /** Télémétrie : « running » quand MicVAD tourne, « absent » s'il n'a jamais démarré. */
    contextState: (): string =>
      vadRef.current ? (listeningRef.current ? 'running' : 'paused') : 'absent',
  };
};

/** Float32 PCM [-1,1] vers Int16 PCM, format attendu par le WebSocket vocal. */
function floatTo16BitPCM(float32: Float32Array): Int16Array {
  const int16 = new Int16Array(float32.length);
  for (let i = 0; i < float32.length; i++) {
    const s = Math.max(-1, Math.min(1, float32[i]));
    int16[i] = s < 0 ? s * 0x8000 : s * 0x7fff;
  }
  return int16;
}
