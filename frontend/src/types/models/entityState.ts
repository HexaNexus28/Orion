// entityState.ts - État de l'entité ORION
export type EntityState = 'idle' | 'listening' | 'thinking' | 'responding' | 'error';

// Alias pour backward compatibility
export type OrionState = EntityState;
