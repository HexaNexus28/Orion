import React, { useCallback, useEffect, useRef, useState } from 'react';
import { ParticleCanvas } from './components/ui/ParticleCanvas';
import { OrionEntity } from './components/ui/OrionEntity';
import { SlideInput } from './components/input/SlideInput';
import { ResponseText } from './components/ui/ResponseText';
import { MemoryOverlay } from './components/overlay/MemoryOverlay';
import { BriefingOverlay } from './components/overlay/BriefingOverlay';
import { SettingsOverlay } from './components/overlay/SettingsOverlay';
import { useEntity } from './context/EntityContext';
import { useOrionStatus } from './context/OrionStatusContext';
import { useGestureControl } from './hooks/useGestureControl';
import { useVAD } from './hooks/useVAD';
import { useStream } from './hooks/useStream';
import { transcribeBlob } from './services/voiceApi';

const isHandTrackingEnabled = import.meta.env.VITE_ENABLE_HAND_TRACKING === 'true';
const SWIPE_THRESHOLD = 80;

const App: React.FC = () => {
  const { state: entityState, amplitude, setState, setAmplitude, updateAmplitude } = useEntity();
  const { text: responseText, isStreaming, streamMessage, reset } = useStream();
  const { daemonConnected } = useOrionStatus();
  const spokenUpToRef = useRef(0);

  const [isInputVisible, setIsInputVisible] = useState(false);
  const [isMemoryOpen, setIsMemoryOpen] = useState(false);
  const [isBriefingOpen, setIsBriefingOpen] = useState(false);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
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
    if (isInputVisible || isMemoryOpen || isBriefingOpen || isSettingsOpen) return;

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
  useEffect(() => {
    if (!responseText) {
      spokenUpToRef.current = 0;
      return;
    }

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
  }, [responseText, isStreaming, speakSentence]);

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
    stopTTS(); // Coupe ORION immédiatement si tu commences à parler
  }, [unlockSpeech, setState, setAmplitude, stopTTS]);

  // Ref pour stocker l'audio reçu du VAD
  const audioBlobRef = useRef<Blob | null>(null);

  const handleAudioReady = useCallback((blob: Blob) => {
    audioBlobRef.current = blob;
    setAmplitude(0); // Reset le pulse quand la parole se termine
  }, [setAmplitude]);

  const { isSpeaking, isListening, start: startVAD, pause: pauseVAD, reset: resetVAD } = useVAD({
    onSpeechStart: handleSpeechStart,
    onAudioReady: handleAudioReady,
    onError: handleVoiceError,
  });

  // ── Input controls ───────────────────────────────────────────────────────────
  const handleOpenInput = useCallback(() => {
    unlockSpeech(); // Déverrouillle TTS dès le premier tap
    setIsInputVisible(true);
  }, [unlockSpeech]);
  const handleCloseInput = useCallback(() => setIsInputVisible(false), []);
  const handleOpenSettings = useCallback(() => setIsSettingsOpen(true), []);

  // ── Passive listening ────────────────────────────────────────────────────────
  const startPassiveListening = useCallback(async () => {
    if (isInputVisible || isStreaming || isTTSSpeaking || isProcessingVoiceRef.current || isPassiveListeningRef.current) {
      return;
    }
    console.log('[App] startPassiveListening → démarrage VAD');
    try {
      audioBlobRef.current = null;
      resetVAD();
      await startVAD();
      isPassiveListeningRef.current = true;
      setVoiceError(null);
      setState('idle');
      console.log('[App] Écoute passive active');
    } catch (err) {
      console.error('[App] Erreur startPassiveListening:', err);
      isPassiveListeningRef.current = false;
    }
  }, [isInputVisible, isStreaming, isTTSSpeaking, resetVAD, setState, startVAD]);

  const stopPassiveListening = useCallback(async () => {
    if (!isPassiveListeningRef.current) return null;
    isPassiveListeningRef.current = false;
    setAmplitude(0);
    pauseVAD(); // ← Pause MicVAD, garde l'audio en mémoire via onAudioReady
    // Retourne le blob stocké par onAudioReady
    return audioBlobRef.current;
  }, [setAmplitude, pauseVAD]);

  // ── Voice turn processing ────────────────────────────────────────────────────
  const processVoiceTurn = useCallback(async () => {
    if (isProcessingVoiceRef.current || isInputVisible) return;

    isProcessingVoiceRef.current = true;
    reset();
    setState('thinking');

    try {
      const audioBlob = await stopPassiveListening();
      if (!audioBlob || audioBlob.size < 8000) {
        // < 8 KB WAV = moins de ~250ms de parole — trop court, skip silencieux
        console.log('[Voice] Audio trop court (' + (audioBlob?.size ?? 0) + ' bytes), skip');
        setState('idle');
        return;
      }

      // Forcer le français — évite les hallucinations Whisper en auto-detect
      const transcription = await transcribeBlob(audioBlob, 'fr');
      if (!transcription.success || !transcription.data) {
        throw new Error(transcription.message || 'Transcription échouée');
      }

      const transcript = transcription.data.transcript.trim();

      // Filtres anti-hallucination Whisper
      if (!transcript || transcript.length < 3) {
        console.log('[Voice] Transcription trop courte, ignorée');
        setState('idle');
        return;
      }

      // Tags de bruit Whisper (musique, applaudissements, etc.)
      const whisperNoisePattern = /^\s*\[.*?\]\s*$|\[Musique\]|\[Music\]|\[Applaudissements?\]|\[Rires?\]|\[Silence\]|\[Bruit\]/i;
      if (whisperNoisePattern.test(transcript)) {
        console.log('[Voice] Hallucination Whisper détectée, ignorée:', transcript);
        setState('idle');
        return;
      }

      // Scripts non-latins excessifs (arabe, cyrillique, grec)
      const suspiciousChars = [...transcript].filter(c => /[\u0600-\u06FF\u0400-\u04FF\u0370-\u03FF]/u.test(c)).length;
      if (suspiciousChars > transcript.length * 0.2) {
        console.warn('[Voice] Script suspect, ignoré:', transcript);
        setState('idle');
        return;
      }

      setState('responding');
      await streamMessage(transcript);
      setState('idle');
    } catch (error) {
      const msg = error instanceof Error ? error.message : 'Erreur de traitement vocal';
      console.error('[Voice] Processing error:', error);
      setVoiceError(msg);
      setState('error');
      setTimeout(() => setState('idle'), 5000);
    } finally {
      isProcessingVoiceRef.current = false;
      audioBlobRef.current = null; // Clear pour prochain tour
      await new Promise(r => setTimeout(r, 300)); // Wait MicVAD cleanup
      if (!isInputVisible) await startPassiveListening();
    }
  }, [isInputVisible, reset, setState, startPassiveListening, stopPassiveListening, streamMessage]);

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

  // ── Passive listening lifecycle ──────────────────────────────────────────────
  // VAD bloqué si : input ouvert, streaming en cours, OU ORION est en train de parler
  useEffect(() => {
    if (isInputVisible || isStreaming || isTTSSpeaking) {
      void stopPassiveListening();
      return;
    }
    void startPassiveListening();
    return () => { void stopPassiveListening(); };
  }, [isInputVisible, isStreaming, isTTSSpeaking, startPassiveListening, stopPassiveListening]);

  // ── VAD → trigger voice turn ─────────────────────────────────────────────────
  // Quand MicVAD détecte la fin de parole (isSpeaking passe false → true → false)
  // et qu'on a reçu l'audio, on déclenche le traitement
  useEffect(() => {
    if (
      !isSpeaking && // Fin de parole détectée
      audioBlobRef.current && // Audio prêt
      !isInputVisible &&
      isPassiveListeningRef.current &&
      !isProcessingVoiceRef.current
    ) {
      void processVoiceTurn();
    }
  }, [isSpeaking, isInputVisible, processVoiceTurn]);

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
  useEffect(() => {
    if (!prevDaemonRef.current && daemonConnected) {
      // Daemon just connected — brief visual feedback via entity state
      setState('responding');
      setTimeout(() => setState('idle'), 600);
    }
    prevDaemonRef.current = daemonConnected;
  }, [daemonConnected, setState]);

  return (
    <div
      className="fixed inset-0 overflow-hidden bg-orion-darker"
      onTouchStart={handleTouchStart}
      onTouchEnd={handleTouchEnd}
    >
      {/* Layer 0 — fond particules */}
      <ParticleCanvas intensity={0.5} />

      {/* Layer 1 — surface unique : entité + réponse */}
      <div className="absolute inset-0 flex flex-col items-center justify-center gap-8 z-10 pointer-events-none">
        <div className="pointer-events-auto">
          <OrionEntity
            state={entityState}
            amplitude={amplitude}
            onTap={handleOpenInput}
            onLongPress={processVoiceTurn}
            onDoubleTap={handleOpenSettings}
          />
        </div>

        {responseText && (
          <div className="pointer-events-auto w-full max-w-2xl px-4">
            <ResponseText
              text={responseText}
              isStreaming={isStreaming}
              speed="normal"
            />
          </div>
        )}
      </div>

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

      {/* Hand tracking video (caché) */}
      {isHandTrackingEnabled && (
        <video ref={videoRef} className="hidden" autoPlay muted playsInline />
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
      </div>
    </div>
  );
};

export default App;
