import type { EntityState } from '../types';

// Calculate RMS (Root Mean Square) for audio amplitude
export const calculateRMS = (samples: Float32Array): number => {
  let sum = 0;
  for (let i = 0; i < samples.length; i++) {
    sum += samples[i] * samples[i];
  }
  return Math.sqrt(sum / samples.length);
};

// Interpolation utilities for smooth animations
export const lerp = (start: number, end: number, factor: number): number => {
  return start + (end - start) * factor;
};

export const easeOutCubic = (t: number): number => {
  return 1 - Math.pow(1 - t, 3);
};

export const easeInOutSine = (t: number): number => {
  return -(Math.cos(Math.PI * t) - 1) / 2;
};

// State-based animation configurations
export const getStateConfig = (state: EntityState) => {
  const configs: Record<EntityState, { color: string; speed: number; rings: number }> = {
    idle: { color: '#00d4ff', speed: 0.5, rings: 3 },
    listening: { color: '#00ff88', speed: 2, rings: 4 },
    thinking: { color: '#ffaa00', speed: 3, rings: 5 },
    responding: { color: '#00d4ff', speed: 1.5, rings: 3 },
    error: { color: '#ff4444', speed: 0.3, rings: 2 }
  };
  return configs[state];
};

// Audio amplitude smoothing
export const smoothAmplitude = (
  current: number,
  target: number,
  smoothing: number = 0.3
): number => {
  return lerp(current, target, smoothing);
};
