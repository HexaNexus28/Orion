import { useState, useEffect, ReactNode } from 'react';
import { authService } from '../../services/authService';
import { LoginScreen } from './LoginScreen';

/**
 * Porte d'entree : rien de l'application ne se monte tant qu'il n'y a pas de session valide.
 *
 * Ce n'est pas qu'une politesse d'interface. Les composants d'ORION appellent l'API des leur
 * montage (statut, notifications SSE, historique) : sans cette porte, une session expiree
 * declencherait une volee de 401 et l'utilisateur verrait une application cassee plutot qu'un
 * ecran de connexion.
 *
 * L'evenement `orion:unauthenticated` est emis par l'intercepteur axios — c'est ce qui fait
 * revenir ici quand le jeton expire EN COURS d'utilisation, sans rechargement de page.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const [authenticated, setAuthenticated] = useState(() => authService.isAuthenticated());

  useEffect(() => {
    const onUnauthenticated = () => setAuthenticated(false);
    window.addEventListener('orion:unauthenticated', onUnauthenticated);
    return () => window.removeEventListener('orion:unauthenticated', onUnauthenticated);
  }, []);

  if (!authenticated) {
    return <LoginScreen onAuthenticated={() => setAuthenticated(true)} />;
  }

  return <>{children}</>;
}
