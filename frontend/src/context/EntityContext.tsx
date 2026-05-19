import { createContext, useContext, useState, useRef, useMemo, ReactNode, useCallback, MutableRefObject } from 'react';

export type OrionState = 'idle' | 'listening' | 'thinking' | 'responding' | 'error';

interface EntityContextType {
  state: OrionState;
  setState: (state: OrionState) => void;
  setAmplitude: (amp: number) => void;
  updateAmplitude: () => void;
  amplitudeRef: MutableRefObject<number>;
}

const EntityContext = createContext<EntityContextType | null>(null);

export const EntityProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [state, setStateState] = useState<OrionState>('idle');

  // Amplitude is ref-based — no React state, no re-renders at 60fps
  const amplitudeRef = useRef(0);
  const targetAmplitudeRef = useRef(0);

  const setState = useCallback((newState: OrionState) => {
    setStateState(newState);
  }, []);

  const setAmplitude = useCallback((amp: number) => {
    targetAmplitudeRef.current = Math.max(0, Math.min(1, amp));
  }, []);

  // Pure ref lerp — no setState, no React re-renders triggered
  const updateAmplitude = useCallback(() => {
    amplitudeRef.current += (targetAmplitudeRef.current - amplitudeRef.current) * 0.3;
  }, []);

  // Context only changes when `state` changes — not on every amplitude update
  const contextValue = useMemo(() => ({
    state,
    setState,
    setAmplitude,
    updateAmplitude,
    amplitudeRef,
  }), [state, setState, setAmplitude, updateAmplitude]);

  return (
    <EntityContext.Provider value={contextValue}>
      {children}
    </EntityContext.Provider>
  );
};

export const useEntity = () => {
  const context = useContext(EntityContext);
  if (!context) {
    throw new Error('useEntity must be used within EntityProvider');
  }
  return context;
};
