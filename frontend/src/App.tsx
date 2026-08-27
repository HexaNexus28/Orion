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
  const spokenUpToRef = useRef(0);
  const voiceWSResponseRef = useRef(false); // true = response from WS, skip Web Speech TTS


  const [isInputVisible, setIsInputVisible] = useState(false);
  const [isMemoryOpen, setIsMemoryOpen] = useState(false);
  const [isBriefingOpen, setIsBriefingOpen] = useState(false);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [isDeferredOpen, setIsDeferredOpen] = useState(false);
  const [voiceError, setVoiceError] = useState<string | null>(null);

  const isPassiveListeningRef = useRef(false);
  const isProcessingVoiceRef = useRef(false);
  const touchStartYRef = useRef<number | null>(null);
  const speechUnlockedRef = useRef(false);
  const pendingUtterancesRef = useRef(0);

  const [isTTSSpeaking, setIsTTSSpeaking] = useState(false);

  // ── Swipe detection ──────────────────────────────────────────────────────────
  const handleTouchStart = useCallback((e: React.TouchEvent) => {
    touchStartYRef.current = e.touches[0].clientY;
  }, []);

  const handleTouchEnd = useCallback((e: React.TouchEvent) => {
    if (touchStartYRef.current === null) return;
    const deltaY = touchStartYRef.current - e.changedTouches[0].clientY;
    touchStartYRef.current = null;

    // Only trigger swipe when no overlay/input is open
    if (isInputVisible || isMemoryOpen || isBriefingOpen || isSettingsOpen || isDeferredOpen) return;

    if (deltaY > SWIPE_THRESHOLD) {
      setIsMemoryOpen(true);       // swipe up → mémoire
    } else if (deltaY < -SWIPE_THRESHOLD) {
      setIsBriefingOpen(true);     // swipe down → briefing
    }
  }, [isInputVisible, isMemoryOpen, isBriefingOpen, isSettingsOpen]);

  // ── Sélection de voix française — préférence voix neurales/naturelles ───────
  const getBestFrenchVoice = useCallback((): SpeechSynthesisVoice | undefined => {
    const voices = window.speechSynthesis.getVoices();
    const fr = voices.filter(v => v.lang.startsWith('fr'));
    if (!fr.length) return undefined;
    // 1. Voix neurales Windows Edge (Eva, Denise, Elsa = Natural)
    const natural = fr.find(v => v.name.includes('Natural') || v.name.includes('Eva') || v.name.includes('Denise') || v.name.includes('Elsa'));
    if (natural) return natural;
    // 2. Google Français (Chrome — qualité correcte)
    const google = fr.find(v => v.name.includes('Google'));
    if (google) return google;
    // 3. N'importe quelle voix féminine sauf Hortense (très robotique)
    const decent = fr.find(v => !v.name.includes('Hortense'));
    return decent ?? fr[0];
  }, []);

  // ── Déverrouillage Web Speech API (Chrome exige un geste utilisateur) ────────
  const unlockSpeech = useCallback(() => {
    if (speechUnlockedRef.current || !('speechSynthesis' in window)) return;
    speechUnlockedRef.current = true;
    // Utterance silencieuse pour débloquer l'API
    const unlock = new SpeechSynthesisUtterance('');
    unlock.volume = 0;
    unlock.onend = () => window.speechSynthesis.cancel();
    window.speechSynthesis.speak(unlock);
    // Charger les voix si pas encore disponibles
    if (!window.speechSynthesis.getVoices().length) {
      window.speechSynthesis.onvoiceschanged = () => {
        window.speechSynthesis.onvoiceschanged = null;
      };
    }
  }, []);

  // Arrêt TTS complet + reset état
  const stopTTS = useCallback(() => {
    if ('speechSynthesis' in window) window.speechSynthesis.cancel();
    pendingUtterancesRef.current = 0;
    setIsTTSSpeaking(false);
  }, []);

  const speakSentence = useCallback((text: string) => {
    if (!('speechSynthesis' in window) || !text) return;

    pendingUtterancesRef.current++;
    setIsTTSSpeaking(true); // VAD bloqué pendant qu'ORION parle

    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = 'fr-FR';
    utterance.rate = 1.35;
    utterance.pitch = 1.0;
    utterance.volume = 1;
    const voice = getBestFrenchVoice();
    if (voice) utterance.voice = voice;

    const onDone = () => {
      pendingUtterancesRef.current = Math.max(0, pendingUtterancesRef.current - 1);
      if (pendingUtterancesRef.current === 0 && !window.speechSynthesis.speaking) {
        // Attendre que l'écho meure avant de réécouter
        setTimeout(() => setIsTTSSpeaking(false), 1200);
      }
    };
    utterance.onend = onDone;
    utterance.onerror = onDone;

    window.speechSynthesis.speak(utterance);
  }, [getBestFrenchVoice]);

  // ── TTS : parle phrase par phrase pendant le stream (Web Speech API) ────────
  // Only active for TEXT input mode. When voice WS pipeline is active (isTurnActive),
  // TTS audio comes from Kokoro via WebSocket — don't double-speak.
  useEffect(() => {
    if (!responseText) {
      spokenUpToRef.current = 0; // Reset cursor only on new conversation (empty text)
      return;
    }
    if (isTTSSpeaking || voiceWSResponseRef.current) return; // Don't reset cursor here!

    const unspoken = responseText.slice(spokenUpToRef.current);
    const sentenceRegex = /[^.!?\n]+[.!?\n]+/g;
    let match: RegExpExecArray | null;
    let lastMatchEnd = 0;

    while ((match = sentenceRegex.exec(unspoken)) !== null) {
      const sentence = match[0].trim().replace(/[*_`#>|]/g, '').trim();
      if (sentence.length > 3) speakSentence(sentence);
      lastMatchEnd = match.index + match[0].length;
    }

    if (lastMatchEnd > 0) spokenUpToRef.current += lastMatchEnd;

    if (!isStreaming) {
      const remaining = responseText.slice(spokenUpToRef.current).trim().replace(/[*_`#>|]/g, '').trim();
      if (remaining.length > 3) speakSentence(remaining);
      spokenUpToRef.current = responseText.length;
    }
  }, [responseText, isStreaming, isTTSSpeaking, speakSentence]);

  // ── Voice error handling ─────────────────────────────────────────────────────
  const handleVoiceError = useCallback((error: string) => {
    setVoiceError(error);
    setAmplitude(0);
    setState('error');
    isPassiveListeningRef.current = false;
  }, [setAmplitude, setState]);

  const handleSpeechStart = useCallback(() => {
    unlockSpeech();
    setVoiceError(null);
    setState('listening');
    setAmplitude(0.6);
    stopTTS(); // Coupe Web Speech TTS
  }, [unlockSpeech, setState, setAmplitude, stopTTS]);

  // Ref pour stocker l'audio reçu du VAD
  const audioBlobRef = useRef<Blob | null>(null);

  const handleAudioReady = useCallback((blob: Blob) => {
    audioBlobRef.current = blob;
    setAmplitude(0); // Reset le pulse quand la parole se termine
  }, [setAmplitude]);

  // sendAudioRef used to forward PCM chunks to WebSocket from VAD (avoids circular deps)
  const sendAudioRef = useRef<((pcm16: Int16Array) => void) | null>(null);

  // Telemetrie du micro : le serveur ne peut pas distinguer « contexte en pause » de « parole
  // trop faible » — les deux donnent le meme silence. On mesure donc ici et on rapporte.
  // Le micro ne démarre QUE sur un geste. Ce n’est pas un choix ergonomique, c’est la
  // plateforme : un navigateur refuse la capture audio tant que l’utilisateur n’a rien touché,
  // et il le refuse EN SILENCE — AudioContext « suspended », zéro octet, aucune erreur.
  // Démarrer au montage revenait à espérer que le navigateur ferait une exception.
  //
  // Google Assistant ne fait pas autrement dans un navigateur : le mot-clé « OK Google » est
  // détecté par une couche NATIVE, dont une PWA ne dispose pas.
  const [micArmed, setMicArme] = useState(false);

  const maxAmpRef = useRef(0);
  const chunksRef = useRef(0);

  const { isSpeaking, isListening, start: startVAD, pause: pauseVAD, reset: _resetVAD, contextState } = useVAD({
    onSpeechStart: handleSpeechStart,
    onAudioReady: handleAudioReady,
    onAudioChunk: (pcm16) => {
      chunksRef.current += 1;
      sendAudioRef.current?.(pcm16);
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
    unlockSpeech();      // débloque aussi la synthèse vocale, soumise à la même règle
    setMicArme(true);
    setVoiceError(null);
  }, [unlockSpeech]);

  const handleOpenInput = useCallback(() => {
    unlockSpeech(); // Déverrouillle TTS dès le premier tap
    setIsInputVisible(true);
  }, [unlockSpeech]);
  const handleCloseInput = useCallback(() => setIsInputVisible(false), []);
  const handleOpenSettings = useCallback(() => setIsSettingsOpen(true), []);

  // ── Passive listening (ref-based to avoid re-render loops) ─────────────────────
  const startPassiveListeningRef = useRef<() => Promise<void>>(undefined);
  startPassiveListeningRef.current = async () => {
    if (isInputVisible || isProcessingVoiceRef.current || isPassiveListeningRef.current) {
      return;
    }
    console.log('[App] startPassiveListening → démarrage VAD');
    try {
      audioBlobRef.current = null;
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
  const { isTurnActive, sendAudio, endAudio, interrupt, sendDiagnostic } = useVoiceWS({
    onTranscript: (transcript) => {
      console.log('[App] Transcript reçu:', transcript);
      voiceWSResponseRef.current = true; // Mark: this response comes from voice WS
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
      // Keep isStreaming=true until TTS finishes — text stays "live" while ORION speaks
      // setStreaming(false) will be called by onOrionSpeaking(false) via isTurnActive
      spokenUpToRef.current = fullText.length;
    },
    onOrionSpeaking: (speaking) => {
      if (speaking) {
        setState('responding');
        setIsTTSSpeaking(true);
        setStreaming(true); // Keep text in "streaming" mode during audio playback
      } else {
        setState('idle');
        setIsTTSSpeaking(false);
        setStreaming(false); // Text locks when ORION finishes speaking
        voiceWSResponseRef.current = false;
      }
    },
    onAmplitude: () => {
      // NE PAS ecrire dans `amplitude` : cette mesure est celle de la voix qu ORION JOUE,
      // pas de ce que le micro entend. Les deux finissaient dans la meme variable, et le
      // barge-in (amplitudeRef > 0,04) pouvait donc interrompre ORION en entendant ORION.
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

  // Barge-in: quand l'utilisateur parle pendant que ORION est en train de répondre
  // Ignore echo: only barge-in if amplitude is strong (user speaking into mic, not speaker echo)
  const bargeInThreshold = 0.04; // Higher than SPEECH_THRESHOLD (0.015) to avoid echo
  useEffect(() => {
    if (isSpeaking && isTurnActive && amplitudeRef.current > bargeInThreshold) {
      console.log('[App] Barge-in: interruption du tour ORION (amp:', amplitudeRef.current.toFixed(3), ')');
      interrupt();
      audioBlobRef.current = null; // Discard echo audio
    }
  }, [isSpeaking, isTurnActive, interrupt]); // amplitudeRef is a ref — not a dep

  // ── Voice turn processing (WebSocket full-duplex) ──────────────────────────
  // With WebSocket, "processVoiceTurn" just signals end_audio.
  // Audio is streamed in real-time via VAD's onAudioChunk → sendAudio.
  const processVoiceTurn = useCallback(async () => {
    if (isProcessingVoiceRef.current || isInputVisible) return;

    isProcessingVoiceRef.current = true;
    setState('thinking');

    // Consommer la prise : sans ça, `audioBlobRef` reste non-nul après le tour et la condition
    // ligne ~375 redevient vraie dès que `isTurnActive` retombe (fin de réponse d'ORION).
    // Un tour fantôme repartait alors sur le bruit ambiant accumulé côté serveur — ORION
    // répondait à côté, comme s'il n'avait pas écouté. Le déclencheur doit être un FRONT
    // (une prise = un tour), pas un état permanent.
    audioBlobRef.current = null;

    // Tell WebSocket server that speech ended → triggers STT + LLM + TTS
    endAudio();

    // Release processing lock after a short delay
    // (the actual response comes asynchronously via WebSocket callbacks)
    setTimeout(() => {
      isProcessingVoiceRef.current = false;
    }, 500);
  }, [isInputVisible, setState, endAudio]);


  // ── Text submit ──────────────────────────────────────────────────────────────
  const handleSubmit = useCallback(async (message: string) => {
    voiceWSResponseRef.current = false; // Text input → allow Web Speech TTS
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
      audioBlobRef.current && // Audio prêt
      !isInputVisible &&
      !isTurnActive && // Don't start new turn while ORION is responding (echo protection)
      !window.speechSynthesis?.speaking && // Don't trigger during Web Speech TTS (notifs, etc.)
      isPassiveListeningRef.current &&
      !isProcessingVoiceRef.current
    ) {
      void processVoiceTurn();
    }
  }, [isSpeaking, isInputVisible, isTurnActive, processVoiceTurn]);

  // ── Telemetrie du micro vers le serveur ──────────────────────────────────────
  // Toutes les 5 s : etat du contexte audio, amplitude maximale vue, morceaux envoyes.
  // C est ce qui permet de trancher a distance entre les deux causes possibles du silence,
  // sans avoir a lire la console d un telephone.
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

  return (
    <div
      className="fixed inset-0 overflow-hidden bg-orion-darker"
      onTouchStart={handleTouchStart}
      onTouchEnd={handleTouchEnd}
    >
      {/* Voile d’armement du micro.

          Tant qu’aucun geste n’a eu lieu, le navigateur REFUSE la capture audio — en silence.
          Plutôt que de tenter et d’échouer sans rien dire, on demande explicitement le geste.
          C’est aussi ce qui débloque la synthèse vocale, soumise à la même règle. */}
      {!micArmed && (
        <button
          onClick={armMicrophone}
          className="absolute inset-0 z-40 flex flex-col items-center justify-center gap-4
                     bg-orion-darker/80 backdrop-blur-sm"
        >
          <span className="relative flex h-20 w-20 items-center justify-center rounded-full
                           border border-cyan-400/40 text-3xl">
            <span className="absolute inset-0 animate-ping rounded-full bg-cyan-400/10" />
            🎙️
          </span>
          <span className="text-sm tracking-[0.2em] uppercase text-cyan-300/80">
            Touche pour activer
          </span>
          <span className="max-w-[15rem] text-center text-[11px] leading-relaxed text-cyan-100/40">
            Le navigateur exige un geste avant d’ouvrir le micro.
          </span>
        </button>
      )}

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
          onOpen={() => setIsDeferredOpen(true)}
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
        onVoiceEnd={() => setState('idle')}
        onClose={handleCloseInput}
        disabled={entityState === 'thinking'}
        state={entityState}
      />

      {/* Overlays — z-30 */}
      <MemoryOverlay isOpen={isMemoryOpen} onClose={() => setIsMemoryOpen(false)} />
      <BriefingOverlay isOpen={isBriefingOpen} onClose={() => setIsBriefingOpen(false)} />
      <SettingsOverlay isOpen={isSettingsOpen} onClose={() => setIsSettingsOpen(false)} />
      <DeferredQueueOverlay
        isOpen={isDeferredOpen}
        onClose={() => setIsDeferredOpen(false)}
        queue={deferredQueue}
      />

      {/* Hand tracking video (caché) */}
      {isHandTrackingEnabled && (
        <video ref={videoRef} className="hidden" autoPlay muted playsInline />
      )}

      {/* Notification proactive du daemon */}
      {lastNotification && !isInputVisible && (
        <div className="absolute top-6 left-4 right-4 z-30 animate-fade-in">
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
