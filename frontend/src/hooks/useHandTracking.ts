import { useState, useEffect, useRef, useCallback, type RefObject } from 'react';
import { createHandTracker, type HandTrackingResult, type HandGesture, type HandTrackerConfig } from '../algorithms/handTracker';

interface UseHandTrackingOptions {
  enabled?: boolean;
  config?: HandTrackerConfig;
  onGesture?: (gesture: HandGesture) => void;
  onGestureDetected?: (result: HandTrackingResult) => void;
  onHandPresent?: (present: boolean) => void;
}

interface UseHandTrackingReturn {
  isInitialized: boolean;
  isRunning: boolean;
  isTracking: boolean;  // Alias pour isRunning (compatibilité)
  lastGesture: HandGesture;
  handPresent: boolean;
  error: string | null;
  start: () => Promise<void>;
  stop: () => void;
  startTracking: () => Promise<void>;  // Alias pour start (compatibilité)
  stopTracking: () => void;  // Alias pour stop (compatibilité)
  videoRef: RefObject<HTMLVideoElement | null>;
}

/**
 * useHandTracking - Hook React pour MediaPipe Hands
 * Détecte les gestes mains via caméra : paume ouverte, poing fermé, pointer, pinch
 *
 * Usage:
 * const { videoRef, lastGesture, handPresent } = useHandTracking({
 *   enabled: true,
 *   onGesture: (gesture) => {
 *     if (gesture === 'open_palm') setListening(true);
 *     if (gesture === 'closed_fist') setListening(false);
 *   }
 * });
 *
 * <video ref={videoRef} style={{ display: 'none' }} />
 */
export const useHandTracking = (options: UseHandTrackingOptions = {}): UseHandTrackingReturn => {
  const { enabled = false, config, onGesture, onGestureDetected, onHandPresent } = options;

  const [isInitialized, setIsInitialized] = useState(false);
  const [isRunning, setIsRunning] = useState(false);
  const [lastGesture, setLastGesture] = useState<HandGesture>('none');
  const [handPresent, setHandPresent] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const videoRef = useRef<HTMLVideoElement>(null);
  const trackerRef = useRef<ReturnType<typeof createHandTracker> | null>(null);

  // Initialize tracker
  useEffect(() => {
    if (!enabled || !videoRef.current) return;

    let mounted = true;

    const init = async () => {
      try {
        const tracker = createHandTracker(config);

        await tracker.initialize(videoRef.current!, (result: HandTrackingResult) => {
          if (!mounted) return;

          setLastGesture(result.gesture);
          setHandPresent(result.handPresent);

          onGesture?.(result.gesture);
          onGestureDetected?.(result);
          onHandPresent?.(result.handPresent);
        });

        if (mounted) {
          trackerRef.current = tracker;
          setIsInitialized(true);
          setError(null);
        }
      } catch (err) {
        if (mounted) {
          setError(err instanceof Error ? err.message : 'Failed to initialize hand tracking');
          setIsInitialized(false);
        }
      }
    };

    init();

    return () => {
      mounted = false;
      trackerRef.current?.dispose();
      trackerRef.current = null;
    };
  }, [enabled, config, onGesture, onGestureDetected, onHandPresent]);

  const start = useCallback(async () => {
    if (!trackerRef.current || !isInitialized) {
      setError('Hand tracker not initialized');
      return;
    }

    try {
      await trackerRef.current.start();
      setIsRunning(true);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start hand tracking');
      setIsRunning(false);
    }
  }, [isInitialized]);

  const stop = useCallback(() => {
    trackerRef.current?.stop();
    setIsRunning(false);
  }, []);

  // Auto-start when initialized if enabled
  useEffect(() => {
    if (isInitialized && enabled && !isRunning) {
      start();
    }
  }, [isInitialized, enabled, isRunning, start]);

  // Stop when disabled
  useEffect(() => {
    if (!enabled && isRunning) {
      stop();
    }
  }, [enabled, isRunning, stop]);

  return {
    isInitialized,
    isRunning,
    isTracking: isRunning,
    lastGesture,
    handPresent,
    error,
    start,
    stop,
    startTracking: start,
    stopTracking: stop,
    videoRef,
  };
};

export default useHandTracking;
