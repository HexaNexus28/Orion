import { useState, useCallback } from 'react';
import type { EntityState } from '../types';

interface OrionEntityState {
  state: EntityState;
  amplitude: number;
  targetAmplitude: number;
}

export const useOrionEntity = () => {
  const [entityState, setEntityState] = useState<OrionEntityState>({
    state: 'idle',
    amplitude: 0,
    targetAmplitude: 0
  });

  const setState = useCallback((state: EntityState) => {
    setEntityState(prev => ({ ...prev, state }));
  }, []);

  const setAmplitude = useCallback((amplitude: number) => {
    setEntityState(prev => ({
      ...prev,
      targetAmplitude: Math.max(0, Math.min(1, amplitude))
    }));
  }, []);

  const updateAmplitude = useCallback((smoothing: number = 0.3) => {
    setEntityState(prev => ({
      ...prev,
      amplitude: prev.amplitude + (prev.targetAmplitude - prev.amplitude) * smoothing
    }));
  }, []);

  return {
    state: entityState.state,
    amplitude: entityState.amplitude,
    setState,
    setAmplitude,
    updateAmplitude
  };
};
