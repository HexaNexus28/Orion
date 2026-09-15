import React, { useCallback, useEffect, useRef, useState } from 'react';
import { SlideInput } from './components/input/SlideInput';
import { Scene3D } from './components/canvas/Scene3D';
import { HudZones } from './components/ui/HudZones';
import { MemoryOverlay } from './components/overlay/MemoryOverlay';
import { BriefingOverlay } from './components/overlay/BriefingOverlay';
import { SettingsOverlay } from './components/overlay/SettingsOverlay';
import { DeferredQueueOverlay } from './components/overlay/DeferredQueueOverlay';
import { DeferredQueueBadge } from './components/overlay/DeferredQueueBadge';
import { useEntity } from './context/EntityContext';
import { useOrionStatus } from './context/OrionStatusContext';
import { useGestureControl } from './hooks/useGestureControl';
import { useVAD } from './hooks/useVAD';
import { useVoiceWS } from './hooks/useVoiceWS';
import { useStream } from './hooks/useStream';
import { ToolActivityStrip } from './components/overlay/ToolActivityStrip';
import { VoiceStatusHint } from './components/overlay/VoiceStatusHint';
import { useOrionNotifications } from './hooks/useOrionNotifications';
import { useDeferredQueue } from './services/deferredService';

const isHandTrackingEnabled = import.meta.env.VITE_ENABLE_HAND_TRACKING === 'true';
const SWIPE_THRESHOLD = 80;

const App: React.FC = () => {
  const { state: entityState, setState, setAmplitude, updateAmplitude, amplitudeRef } = useEntity();
  const { text: responseText, isStreaming, tools: toolActivity, streamMessage, reset, appendChunk, pushTool, setStreaming } = useStream();
  const { daemonConnected } = useOrionStatus();
  const { lastNotification, isConnected: sseConnected } = useOrionNotifications();
  const deferredQueue = useDeferredQueue();


  const [isInputVisible, setIsInputVisible] = useState(false);
  // UNE SEULE SURFACE A LA FOIS.
  //
  // C'etaient quatre booleens independants, et rien n'empechait d'en ouvrir plusieurs : « m »
  // puis « b » au clavier, ou deux clics de boutons, et deux panneaux pleins se superposaient —
  // tous les quatre a z-30, donc empiles dans l'ordre du DOM, c'est-a-dire au hasard. Seul le
  // swipe se protegeait ; ni les boutons ni les raccourcis ne le faisaient.
  //
  // Un etat unique rend l'exclusion STRUCTURELLE : ouvrir une surface ferme l'autre, sans avoir
  // a penser a la fermer a chaque point d'appel. C'est le genre de garde qu'on n'oublie pas.
  const [activeOverlay, setActiveOverlay] = useState<'memory' | 'briefing' | 'settings' | 'deferred' | null>(null);
  const closeOverlay = useCallback(() => setActiveOverlay(null), []);
  const [voiceError, setVoiceError] = useState<string | null>(null);

  const isPassiveListeningRef = useRef(false);
  const isProcessingVoiceRef = useRef(false);
  const touchStartYRef = useRef<number | null>(null);

  // ── Swipe detection ──────────────────────────────────────────────────────────
  const handleTouchStart = useCallback((e: React.TouchEvent) => {
    touchStartYRef.current = e.touches[0].clientY;
  }, []);

  const handleTouchEnd = useCallback((e: React.TouchEvent) => {
    if (touchStartYRef.current === null) return;
    const deltaY = touchStartYRef.current - e.changedTouches[0].clientY;
    touchStartYRef.current = null;

    // Only trigger swipe when no overlay/input is open
    if (isInputVisible || activeOverlay) return;

    if (deltaY > SWIPE_THRESHOLD) {
      setActiveOverlay('memory');   // swipe up → mémoire
    } else if (deltaY < -SWIPE_THRESHOLD) {
      setActiveOverlay('briefing'); // swipe down → briefing
    }
  }, [isInputVisible, activeOverlay]);

  // ORION PARLE QUAND ON LUI PARLE. Le mode texte ne declenche plus de synthese.
  //
  // Web Speech API a ete retiree d'ORION. Elle passe par le moteur TTS du SYSTEME, hors de
  // portee de l'annulation d'echo du navigateur — qui n'annule que ce qu'IL joue. Sa voix
  // repartait donc dans le micro et ORION se re-ecoutait : c'est la cause qu'aucun reglage de
  // seuil ne pouvait fermer, parce qu'il n'existe aucun signal de reference pour distinguer
  // son echo de ta voix.
  //
  // Reste UN seul chemin sonore dans le navigateur : les WAV du WebSocket vocal, joues par
  // AudioContext — que l'annulation d'echo, elle, voit. Ecris a ORION, il repond par ecrit ;
  // parle-lui, il repond de vive voix.

  // ── Voice error handling ─────────────────────────────────────────────────────
  const handleVoiceError = useCallback((error: string) => {
    setVoiceError(error);
    setAmplitude(0);
    setState('error');
    isPassiveListeningRef.current = false;
  }, [setAmplitude, setState]);

  /**
   * Distingue les deux choses acoustiquement identiques qui commencent pendant qu'ORION parle :
   * son écho (à jeter) et l'utilisateur qui le coupe (à garder). Le volume ne les sépare pas —
   * au haut-parleur sa voix est souvent la plus forte — seul un barge-in DÉCLARÉ le fait.
   */
  const isTurnActiveRef = useRef(false);
  const takeStartedDuringTurnRef = useRef(false);
  const bargeInDeclaredRef = useRef(false);

  const handleSpeechStart = useCallback(() => {
    takeStartedDuringTurnRef.current = isTurnActiveRef.current;
    bargeInDeclaredRef.current = false;
    setVoiceError(null);
    setState('listening');
    setAmplitude(0.6);
  }, [setState, setAmplitude]);

  // Ref pour stocker l'audio reçu du VAD
  // FRONT déclencheur du tour, rien de plus. Ce booléen portait un Blob WAV dont le contenu
  // n'était jamais lu — encodé échantillon par échantillon à chaque prise, pour être jeté.
  const hasCapturedAudioRef = useRef(false);

  const handleSpeechCaptured = useCallback(() => {
    hasCapturedAudioRef.current = true;
    setAmplitude(0); // Reset le pulse quand la parole se termine
  }, [setAmplitude]);

  // sendAudioRef used to forward PCM chunks to WebSocket from VAD (avoids circular deps)
  const sendAudioRef = useRef<((pcm16: Int16Array) => void) | null>(null);

  /**
   * INVARIANT : on n'envoie de l'audio que si on envoie aussi le `end_audio` qui le consomme.
   * Une prise = un tour = un envoi. La prise attend donc ici la décision de `processVoiceTurn`.
   *
   * Émettre sans cette garde renvoyait au serveur l'écho d'ORION capté par le micro (le VAD
   * tourne exprès pendant qu'il parle, pour le barge-in) : il se ré-écoutait. Voir roadmap V1.
   */
  const pendingAudioRef = useRef<Int16Array | null>(null);

  // Telemetrie du micro : le serveur ne peut pas distinguer « contexte en pause » de « parole
  // trop faible » — les deux donnent le meme silence. On mesure donc ici et on rapporte.
  //
  // LE GESTE EST OBLIGATOIRE, LA CÉRÉMONIE NE L'EST PAS. Un navigateur refuse la capture audio
  // tant que l'utilisateur n'a rien touché, et il le refuse EN SILENCE — AudioContext
  // « suspended », zéro octet, aucune erreur. Cette contrainte ne se contourne pas.
  //
  // Mais elle n'exige pas un voile plein écran « touche pour activer » : n'importe quelle
  // interaction satisfait le navigateur. On arme donc au PREMIER CONTACT avec la surface —
  // le tap sur l'entité que l'utilisateur fait de toute façon. Demander un geste dédié
  // ajoutait une étape qui ne servait qu'à nous.
  const [micArmed, setMicArme] = useState(false);
  const micArmedRef = useRef(false);

  const maxAmpRef = useRef(0);
  const chunksRef = useRef(0);

  const { isSpeaking, isListening, start: startVAD, pause: pauseVAD, reset: _resetVAD, contextState } = useVAD({
    onSpeechStart: handleSpeechStart,
    onSpeechCaptured: handleSpeechCaptured,
    // On RETIENT la prise au lieu de l'émettre. C'est `processVoiceTurn` qui décide de son
    // sort, et qui l'envoie collée à son `end_audio`. Une prise non retenue par la décision
    // (écho d'ORION, bruit ambiant pendant qu'il répond) est simplement écrasée par la
    // suivante : elle ne part jamais, donc elle ne peut plus polluer le tour d'après.
    onAudioChunk: (pcm16) => {
      pendingAudioRef.current = pcm16;
    },
    onAmplitude: (amp) => {
      setAmplitude(amp);
      if (amp > maxAmpRef.current) maxAmpRef.current = amp;
    },
    onError: handleVoiceError,
  });

  // ── Input controls ───────────────────────────────────────────────────────────
  /**
   * Le geste qui arme le micro. À appeler depuis un vrai événement utilisateur — c’est ce
   * contexte d’exécution qui autorise le navigateur à démarrer l’audio.
   */
  const armMicrophone = useCallback(() => {
    if (micArmedRef.current) return;
    micArmedRef.current = true;
    setMicArme(true);
    setVoiceError(null);
    console.log('[App] Micro armé par le premier geste');
  }, []);

  // Le clavier compte comme geste : sans ça, qui ouvre la saisie au clavier resterait muet.
  useEffect(() => {
    if (micArmed) return;
    const surPremiereTouche = () => armMicrophone();
    window.addEventListener('keydown', surPremiereTouche, { once: true });
    return () => window.removeEventListener('keydown', surPremiereTouche);
  }, [micArmed, armMicrophone]);

  const handleOpenInput = useCallback(() => {
    setIsInputVisible(true);
  }, []);
  const handleCloseInput = useCallback(() => setIsInputVisible(false), []);
  const handleOpenSettings = useCallback(() => setActiveOverlay('settings'), []);

  // ── Passive listening (ref-based to avoid re-render loops) ─────────────────────
  const startPassiveListeningRef = useRef<() => Promise<void>>(undefined);
  startPassiveListeningRef.current = async () => {
    if (isInputVisible || isProcessingVoiceRef.current || isPassiveListeningRef.current) {
      return;
    }
    console.log('[App] startPassiveListening → démarrage VAD');
    try {
      hasCapturedAudioRef.current = false;
      await startVAD();
      isPassiveListeningRef.current = true;
      setVoiceError(null);
      setState('idle');
      console.log('[App] Écoute passive active');
    } catch (err) {
      console.error('[App] Erreur startPassiveListening:', err);
      isPassiveListeningRef.current = false;
    }
  };

  const stopPassiveListeningRef = useRef<() => void>(undefined);
  stopPassiveListeningRef.current = () => {
    if (!isPassiveListeningRef.current) return;
    isPassiveListeningRef.current = false;
    setAmplitude(0);
    pauseVAD();
  };


  // ── useVoiceWS — Full-duplex WebSocket voice pipeline ─────────────────────
  const { isTurnActive, sendAudio, endAudio, interrupt, sendDiagnostic, isPlayingRef } = useVoiceWS({
    onTranscript: (transcript) => {
      console.log('[App] Transcript reçu:', transcript);
      reset();
      setState('thinking');
      setStreaming(true);
    },
    onLLMChunk: (chunk) => {
      appendChunk(chunk);
    },
    onNoSpeech: () => {
      // Bruit ambiant capte par le VAD : on revient au repos, sans afficher d'erreur.
      setStreaming(false);
      setState('idle');
    },
    onToolStart: (tool, args) => {
      console.log('[App] ORION execute:', tool);
      pushTool({ tool, args, status: 'running', iteration: 0 });
    },
    onToolResult: (tool, ok, summary) => {
      console.log('[App] Outil termine:', tool, ok);
      pushTool({ tool, status: ok ? 'ok' : 'failed', summary, iteration: 0 });
    },
    onLLMDone: (fullText) => {
      console.log('[App] LLM done:', fullText.substring(0, 60) + '...');
      // isStreaming reste vrai jusqu'a la fin de la lecture : le texte demeure « vivant »
      // pendant qu'ORION parle. C'est onOrionSpeaking(false) qui le fige.
    },
    onOrionSpeaking: (speaking) => {
      if (speaking) {
        setState('responding');
        setStreaming(true); // Le texte reste « en cours » pendant la lecture audio
      } else {
        setState('idle');
        setStreaming(false); // Il se fige quand ORION a fini de parler
      }
    },
    onAmplitude: () => {
      // NE PAS ecrire dans `amplitude` : cette mesure est celle de la voix qu ORION JOUE,
      // pas de ce que le micro entend. Les deux finissaient dans la meme variable, et le
      // barge-in pouvait donc interrompre ORION en entendant ORION.
      // Deux grandeurs differentes n ont rien a faire dans un seul etat.
    },
    onError: (err) => {
      setStreaming(false);
      handleVoiceError(err);
    },
  });

  // Wire sendAudio from useVoiceWS to VAD's onAudioChunk via ref
  useEffect(() => {
    sendAudioRef.current = sendAudio;
    return () => { sendAudioRef.current = null; };
  }, [sendAudio]);

  // `handleSpeechStart` est défini AVANT `useVoiceWS` : il ne peut pas lire `isTurnActive`
  // directement. Cette ref est le seul pont, et elle doit rester synchrone avec l'état.
  useEffect(() => {
    isTurnActiveRef.current = isTurnActive;
  }, [isTurnActive]);

  // Barge-in — et surtout : PAS pendant qu'ORION émet du son.
  //
  // La version précédente décidait au VOLUME (seuil 0,04, censé écarter l'écho). Mesuré en
  // usage réel : la voix d'ORION revenue par le haut-parleur arrive à 0,421, soit dix fois
  // au-dessus. Il déclenchait donc un barge-in sur lui-même, s'interrompait, et sa propre
  // phrase repartait au serveur comme une demande.
  //
  // Le volume ne peut pas distinguer l'écho de l'utilisateur : sans signal de référence, la
  // question est indécidable. Mais on sait avec certitude si ORION est en train de JOUER —
  // et pendant ce temps, tout ce que le micro capte est suspect.
  //
  // Ce qu'on garde : couper ORION pendant qu'il RÉFLÉCHIT (tour actif, aucun son émis).
  // Ce qu'on perd : le couper en pleine phrase, qui exige un casque ou une annulation d'écho
  // fonctionnelle — ni l'un ni l'autre n'est garanti sur un téléphone en haut-parleur.
  //
  // PLUS AUCUN SEUIL ICI. Il en restait un — `bargeInThreshold = 0,04` — devenu sans effet le
  // jour où le VAD est passé à Silero : `amplitudeRef` ne porte plus une énergie RMS mais la
  // PROBABILITÉ DE PAROLE du modèle, et cet effet ne s'exécute que si `isSpeaking` est déjà
  // vrai, c'est-à-dire si cette probabilité a franchi 0,5. Comparer ensuite à 0,04 était donc
  // toujours vrai.
  //
  // Un nombre qui ne décide rien mais qui RESSEMBLE à un réglage calibré est pire que son
  // absence : le prochain lecteur l'ajuste, n'observe aucun changement, et va chercher la
  // panne ailleurs. La question « est-ce de la parole humaine ? » est déjà tranchée, et mieux,
  // par Silero — c'est `isSpeaking`.
  useEffect(() => {
    const orionEmet = isPlayingRef.current;

    if (isSpeaking && isTurnActive && !orionEmet) {
      console.log('[App] Barge-in: interruption du tour ORION (p(parole):', amplitudeRef.current.toFixed(3), ')');
      // Le barge-in est DÉCLARÉ : c'est ce drapeau, et lui seul, qui autorise une prise née
      // pendant qu'ORION parlait à devenir un vrai tour. Sans lui, elle est traitée comme
      // l'écho qu'elle est presque toujours.
      bargeInDeclaredRef.current = true;
      interrupt();
      hasCapturedAudioRef.current = false; // Discard echo audio
    }
  }, [isSpeaking, isTurnActive, interrupt]); // amplitudeRef is a ref — not a dep

  // ── Voice turn processing (WebSocket full-duplex) ──────────────────────────
  // SEUL endroit qui émet de l'audio. La prise retenue par `onAudioChunk` part ici, collée au
  // `end_audio` qui la consomme — voir `pendingAudioRef` pour le pourquoi complet.
  const processVoiceTurn = useCallback(async () => {
    if (isProcessingVoiceRef.current || isInputVisible) return;

    // Consommée qu'on la joue ou qu'on l'abandonne : elle ne doit pas survivre à la décision.
    const take = pendingAudioRef.current;
    pendingAudioRef.current = null;

    // Un `end_audio` nu déclencherait un tour sur le tampon serveur, donc sur du bruit. Ce
    // chemin est aussi celui du geste « paume ouverte », qui arrive sans qu'on ait parlé.
    if (!take) {
      console.log('[App] Tour ignoré — aucune prise en attente');
      return;
    }

    if (takeStartedDuringTurnRef.current && !bargeInDeclaredRef.current) {
      console.log('[App] Prise écartée — écho d\'ORION');
      takeStartedDuringTurnRef.current = false;
      hasCapturedAudioRef.current = false;
      return;
    }
    takeStartedDuringTurnRef.current = false;
    bargeInDeclaredRef.current = false;

    isProcessingVoiceRef.current = true;
    setState('thinking');

    // Le déclencheur doit être un FRONT : laissé vrai, il repart dès que `isTurnActive`
    // retombe et ORION répond au bruit ambiant.
    hasCapturedAudioRef.current = false;

    // L'ordre d'émission du WebSocket garantit que la prise arrive avant l'ordre qui la consomme.
    chunksRef.current += 1;
    sendAudioRef.current?.(take);
    endAudio();

    // Release processing lock after a short delay
    // (the actual response comes asynchronously via WebSocket callbacks)
    setTimeout(() => {
      isProcessingVoiceRef.current = false;
    }, 500);
  }, [isInputVisible, setState, endAudio]);


  // ── Text submit ──────────────────────────────────────────────────────────────
  const handleSubmit = useCallback(async (message: string) => {
    reset();
    setState('thinking');
    try {
      setState('responding');
      await streamMessage(message);
      setState('idle');
    } catch (error) {
      console.error('Error:', error);
      setState('error');
      setTimeout(() => setState('idle'), 3000);
    }
  }, [reset, streamMessage, setState]);

  // ── Accès clavier (PC) ───────────────────────────────────────────────────────
  // Les overlays n'étaient atteignables QUE par `handleTouchEnd` : à la souris, aucun événement
  // tactile ne part, donc mémoire et briefing étaient inaccessibles sur ordinateur. Deux
  // fonctionnalités entières, pas deux gestes.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      // Jamais pendant une saisie : on volerait les touches à l'utilisateur.
      const target = e.target as HTMLElement | null;
      if (target && ['INPUT', 'TEXTAREA'].includes(target.tagName)) return;
      if (e.ctrlKey || e.metaKey || e.altKey) return;

      switch (e.key.toLowerCase()) {
        case 'm': setActiveOverlay(v => (v === 'memory' ? null : 'memory')); break;
        case 'b': setActiveOverlay(v => (v === 'briefing' ? null : 'briefing')); break;
        case 'escape': setActiveOverlay(null); break;
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  // ── Animation loop ───────────────────────────────────────────────────────────
  useEffect(() => {
    let animationId = 0;
    const animate = () => {
      updateAmplitude();
      animationId = window.requestAnimationFrame(animate);
    };
    animationId = window.requestAnimationFrame(animate);
    return () => window.cancelAnimationFrame(animationId);
  }, [updateAmplitude]);

  // ── Passive listening lifecycle (avec barge-in) ────────────────────────────
  // VAD tourne en continu SAUF si input texte ouvert.
  // Pendant que ORION parle, le VAD continue → permet barge-in.
  useEffect(() => {
    // `micArme` est la garde qui manquait : tant qu’aucun geste n’a eu lieu, on ne tente même
    // pas la capture. Une fois armé, l’écoute continue reprend seule après chaque tour.
    if (!micArmed || isInputVisible) {
      stopPassiveListeningRef.current?.();
      return;
    }
    void startPassiveListeningRef.current?.();
    return () => { stopPassiveListeningRef.current?.(); };
  }, [micArmed, isInputVisible]);

  // ── VAD → trigger voice turn ─────────────────────────────────────────────────
  // Quand MicVAD détecte la fin de parole (isSpeaking passe false → true → false)
  // et qu'on a reçu l'audio, on déclenche le traitement
  useEffect(() => {
    if (
      !isSpeaking && // Fin de parole détectée
      hasCapturedAudioRef.current && // Audio prêt
      !isInputVisible &&
      !isTurnActive && // Don't start new turn while ORION is responding (echo protection)
      isPassiveListeningRef.current &&
      !isProcessingVoiceRef.current
    ) {
      void processVoiceTurn();
    }
  }, [isSpeaking, isInputVisible, isTurnActive, processVoiceTurn]);

  // ── Telemetrie du micro vers le serveur ──────────────────────────────────────
  // Toutes les 5 s : etat du contexte audio, pic de PROBABILITE DE PAROLE vu, morceaux envoyes.
  // C est ce qui permet de trancher a distance entre les deux causes possibles du silence,
  // sans avoir a lire la console d un telephone.
  //
  // Depuis Silero, `maxAmpRef` n est plus un pic d energie mais la plus forte confiance du
  // modele sur l intervalle. Un micro muet reste proche de 0 ; un micro qui capte sans que ce
  // soit de la parole (ventilateur, musique) reste bas AUSSI — ce que l energie ne disait pas.
  // Un pic voisin de 1 innocente donc definitivement la capture.
  useEffect(() => {
    const t = setInterval(() => {
      sendDiagnostic(contextState(), maxAmpRef.current, chunksRef.current);
      maxAmpRef.current = 0;
    }, 5000);
    return () => clearInterval(t);
  }, [contextState, sendDiagnostic]);

  // ── Hand tracking / gestures ─────────────────────────────────────────────────
  const { videoRef } = useGestureControl({
    enabled: isHandTrackingEnabled,
    onOpenPalm: processVoiceTurn,
    onClosedFist: () => setState('idle'),
    onPointUp: handleOpenInput,
    onPointDown: handleCloseInput,
    onThumbsUp: handleOpenInput,
    onThumbsDown: handleCloseInput,
  });

  // ── Daemon status flash ──────────────────────────────────────────────────────
  const prevDaemonRef = useRef(daemonConnected);
  const refreshDeferred = deferredQueue.refresh;
  useEffect(() => {
    if (!prevDaemonRef.current && daemonConnected) {
      // Daemon just connected — brief visual feedback via entity state
      setState('responding');
      setTimeout(() => setState('idle'), 600);
      // Le backend draine au même moment : la file affichée doit suivre, pas rester d'hier.
      void refreshDeferred();
    }
    prevDaemonRef.current = daemonConnected;
  }, [daemonConnected, setState, refreshDeferred]);

  // Le drain a fini et l'a annoncé : c'est le signal qui fait autorité sur l'état réel de la file.
  useEffect(() => {
    if (lastNotification?.type === 'deferred') {
      void refreshDeferred();
    }
  }, [lastNotification, refreshDeferred]);

  // ── ÉCHELLE DES PLANS, et elle n'a qu'un seul endroit ────────────────────────
  //   z-0   la scène 3D
  //   z-10  l'ambiance (indice d'état vocal)
  //   z-20  ce qui reste affiché : boutons, badges, bande d'outils, notification
  //   z-30  les surfaces modales — UNE SEULE ouverte à la fois
  //   z-40  le voile de la saisie
  //   z-50  la saisie elle-même, et la panne micro qui doit passer devant tout
  //
  // Les quatre overlays partageaient z-30 AVEC la bande d'outils et la notification, qui
  // passaient donc par-dessus un panneau ouvert. À égalité, c'est l'ordre du DOM qui tranche.
  return (
    <div
      className="fixed inset-0 overflow-hidden bg-orion-darker"
      onPointerDown={armMicrophone}
      onTouchStart={handleTouchStart}
      onTouchEnd={handleTouchEnd}
    >
      {/* Panne micro — affichée EN GRAND, au centre.

          Le message existait déjà, mais discret : « Écoute passive active » s’affichait juste
          après un échec, et l’interface donnait tous les signes du bon fonctionnement pendant
          que rien ne marchait. Une panne qui se déguise en succès coûte des heures. */}
      {voiceError && micArmed && (
        <div className="absolute inset-x-0 top-0 z-50 flex justify-center p-4">
          <button
            onClick={() => { setVoiceError(null); void startPassiveListeningRef.current?.(); }}
            className="max-w-md rounded-xl border border-amber-400/40 bg-amber-950/80 px-4 py-3
                       text-left backdrop-blur-sm"
          >
            <p className="text-[10px] uppercase tracking-[0.2em] text-amber-300/70">Micro indisponible</p>
            <p className="mt-1 text-xs leading-relaxed text-amber-100/90">{voiceError}</p>
            <p className="mt-2 text-[10px] text-amber-300/60">Touche ce message pour réessayer.</p>
          </button>
        </div>
      )}

      {/* Canvas 3D — orbe + texte 3D réponse */}
      <Scene3D
        responseText={responseText}
        isStreaming={isStreaming}
        onTap={handleOpenInput}
        onLongPress={processVoiceTurn}
        onDoubleTap={handleOpenSettings}
      />

      {/* Ce qu'ORION fait, et ce que l'utilisateur peut faire maintenant */}
      {!isInputVisible && (
        <VoiceStatusHint
          state={entityState}
          isListening={isListening}
          isSpeaking={isSpeaking}
          micDenied={Boolean(voiceError) && !isListening}
          onRetryMic={() => {
            setVoiceError(null);
            void startPassiveListeningRef.current?.();
          }}
        />
      )}

      {/* Ce qui attend le reveil du PC — n'apparait que s'il y a quelque chose */}
      {!isInputVisible && (
        <DeferredQueueBadge
          enAttente={deferredQueue.enAttente.length}
          aConfirmer={deferredQueue.aConfirmer.length}
          onOpen={() => setActiveOverlay('deferred')}
        />
      )}

      {/* Trace des actions — ce qu'ORION FAIT, pas seulement ce qu'il dit */}
      <ToolActivityStrip tools={toolActivity} />

      {/* Zones du HUD — SANS condition.

          Elles étaient rendues seulement si `!isStreaming && responseText`, donc uniquement
          après une réponse : un widget conditionné à une conversation n'a rien de permanent,
          et l'écran redevenait vide entre deux phrases. HudZones ne rend rien de lui-même
          quand aucune carte ne le mérite — la condition était au mauvais endroit. */}
      <HudZones />

      {/* Layer 2 — input caché, slide depuis le bas */}
      <SlideInput
        isVisible={isInputVisible}
        onSubmit={handleSubmit}
        onClose={handleCloseInput}
        disabled={entityState === 'thinking'}
        state={entityState}
      />

      {/* Commandes visibles — un raccourci clavier ne se découvre pas, et le swipe n'existe
          pas à la souris. Ces deux boutons sont le SEUL chemin vers la mémoire et le briefing
          sur ordinateur.

          À GAUCHE : le coin droit appartient à DeferredQueueBadge, qui y apparaît dès qu'une
          action attend. Les deux s'y superposaient. */}
      <div className="fixed top-4 left-4 z-20 flex gap-2">
        <button
          type="button"
          onClick={() => setActiveOverlay('memory')}
          title="Mémoire (M) — ou glisse vers le haut"
          className="px-3 py-1.5 rounded-lg text-xs bg-black/40 backdrop-blur border border-white/10 text-white/70 hover:text-white hover:border-white/30 transition"
        >
          Mémoire
        </button>
        <button
          type="button"
          onClick={() => setActiveOverlay('briefing')}
          title="Briefing (B) — ou glisse vers le bas"
          className="px-3 py-1.5 rounded-lg text-xs bg-black/40 backdrop-blur border border-white/10 text-white/70 hover:text-white hover:border-white/30 transition"
        >
          Briefing
        </button>
      </div>

      {/* Overlays — z-30, et UN SEUL ouvert a la fois (cf. `activeOverlay`). */}
      <MemoryOverlay isOpen={activeOverlay === 'memory'} onClose={closeOverlay} />
      <BriefingOverlay isOpen={activeOverlay === 'briefing'} onClose={closeOverlay} />
      <SettingsOverlay isOpen={activeOverlay === 'settings'} onClose={closeOverlay} />
      <DeferredQueueOverlay
        isOpen={activeOverlay === 'deferred'}
        onClose={closeOverlay}
        queue={deferredQueue}
      />

      {/* Hand tracking video (caché) */}
      {isHandTrackingEnabled && (
        <video ref={videoRef} className="hidden" autoPlay muted playsInline />
      )}

      {/* Notification proactive du daemon */}
      {lastNotification && !isInputVisible && (
        <div className="absolute top-6 left-4 right-4 z-20 animate-fade-in">
          <div className={`rounded-xl px-4 py-3 backdrop-blur-md border ${
            lastNotification.priority === 'critical' ? 'bg-red-500/20 border-red-500/40 text-red-200' :
            lastNotification.priority === 'high' ? 'bg-orange-500/20 border-orange-500/40 text-orange-200' :
            'bg-orion-accent/10 border-orion-accent/30 text-orion-light/80'
          }`}>
            <p className="text-sm leading-relaxed">{lastNotification.message}</p>
          </div>
        </div>
      )}

      {/* Erreur voix */}
      {voiceError && !isInputVisible && (
        <div className="absolute bottom-10 left-0 right-0 text-center text-sm text-red-400 z-20 px-4">
          {voiceError}
        </div>
      )}

      {/* Statut minimal — points discrets en bas */}
      <div className="absolute bottom-3 left-0 right-0 flex items-center justify-center gap-2 z-10 pointer-events-none">
        <span
          className={`w-1.5 h-1.5 rounded-full transition-colors duration-1000 ${
            entityState === 'idle' ? 'bg-orion-accent/20' : 'bg-orion-accent/60 animate-pulse'
          }`}
          title={entityState}
        />
        {/* VAD actif = point bleu, parole détectée = pulse */}
        <span
          className={`w-1.5 h-1.5 rounded-full transition-colors duration-300 ${
            isSpeaking ? 'bg-blue-400 animate-pulse' : isListening ? 'bg-blue-400/40' : 'bg-gray-500/20'
          }`}
          title={isSpeaking ? 'parole détectée' : isListening ? 'vad actif' : 'vad inactif'}
        />
        <span
          className={`w-1.5 h-1.5 rounded-full transition-colors duration-1000 ${
            daemonConnected ? 'bg-green-500/30' : 'bg-red-500/20'
          }`}
          title={daemonConnected ? 'daemon connecté' : 'daemon déconnecté'}
        />
        <span
          className={`w-1.5 h-1.5 rounded-full transition-colors duration-1000 ${
            sseConnected ? 'bg-purple-400/30' : 'bg-purple-400/10'
          }`}
          title={sseConnected ? 'SSE connecté' : 'SSE déconnecté'}
        />
      </div>
    </div>
  );
};

export default App;
