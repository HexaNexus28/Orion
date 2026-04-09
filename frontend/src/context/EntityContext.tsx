import { createContext, useContext, useState, ReactNode, useCallback } from 'react';

export type OrionState = 'idle' | 'listening' | 'thinking' | 'responding' | 'error';

interface EntityContextType {
  state: OrionState;
  amplitude: number;
  targetAmplitude: number;
  setState: (state: OrionState) => void;
  setAmplitude: (amp: number) => void;
  updateAmplitude: () => void;
}

const EntityContext = createContext<EntityContextType | null>(null);

export const EntityProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [state, setStateState] = useState<OrionState>('idle');
  const [amplitude, setAmp] = useState(0);
  const [targetAmplitude, setTargetAmp] = useState(0);

  const setState = useCallback((newState: OrionState) => {
    setStateState(newState);
  }, []);

  const setAmplitude = useCallback((amp: number) => {
    setTargetAmp(Math.max(0, Math.min(1, amp)));
  }, []);

  const updateAmplitude = useCallback(() => {
    setAmp(prev => prev + (targetAmplitude - prev) * 0.3);
  }, [targetAmplitude]);

  return (
    <EntityContext.Provider value={{
      state,
      amplitude,
      targetAmplitude,
      setState,
      setAmplitude,
      updateAmplitude
    }}>
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
