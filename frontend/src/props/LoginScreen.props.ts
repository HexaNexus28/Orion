export interface LoginScreenProps {
  /** Appele apres une connexion reussie, pour que l'application se monte. */
  onAuthenticated: () => void;
}
