import { useState, useCallback, useRef, useEffect } from 'react';
import { calculateRMS, smoothAmplitude } from '../utils/animationUtils';

interface AudioAmplitudeState {
  amplitude: number;
  isActive: boolean;
}

export const useAudioAmplitude = (smoothing: number = 0.3) => {
  const [state, setState] = useState<AudioAmplitudeState>({
    amplitude: 0,
    isActive: false
  });

  const currentAmplitudeRef = useRef(0);
  const rafIdRef = useRef<number | null>(null);

  const processAudioData = useCallback((audioData: Float32Array) => {
    const rms = calculateRMS(audioData);
    const normalized = Math.min(rms * 4, 1); // Boost for visibility

    currentAmplitudeRef.current = smoothAmplitude(
      currentAmplitudeRef.current,
      normalized,
      smoothing
    );

    setState({
      amplitude: currentAmplitudeRef.current,
      isActive: currentAmplitudeRef.current > 0.01
    });
  }, [smoothing]);

  const reset = useCallback(() => {
    currentAmplitudeRef.current = 0;
    setState({ amplitude: 0, isActive: false });
    if (rafIdRef.current) {
      cancelAnimationFrame(rafIdRef.current);
    }
  }, []);

  useEffect(() => {
    return () => {
      if (rafIdRef.current) {
        cancelAnimationFrame(rafIdRef.current);
      }
    };
  }, []);

  return {
    amplitude: state.amplitude,
    isActive: state.isActive,
    processAudioData,
    reset
  };
};
