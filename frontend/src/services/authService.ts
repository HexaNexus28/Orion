import axios from 'axios';
import { API_BASE } from '../config/endpoints';

/**
 * Authentification d'ORION — mot de passe unique echange contre un jeton de session.
 *
 * POURQUOI PAS UN SECRET EMBARQUE. Ce bundle tourne dans un navigateur : n'importe qui
 * chargeant la page peut le lire. Un secret dedans ne protegerait rien. Le mot de passe est
 * SAISI par l'utilisateur, echange contre un jeton a duree limitee, et seul ce jeton est
 * conserve — rien de permanent ne vit cote client.
 *
 * localStorage et non sessionStorage : sur telephone, l'app est fermee et rouverte en
 * permanence. Une session par onglet obligerait a retaper le mot de passe dix fois par jour,
 * ce qui pousse a choisir un mot de passe faible — la securite theorique se paie en securite reelle.
 */
const TOKEN_KEY = 'orion.token';
const EXPIRY_KEY = 'orion.token.expiresAt';

export interface LoginResult {
  token: string;
  expiresAt: string;
}

export const authService = {
  /** Jeton courant, ou null s'il est absent ou EXPIRE. */
  getToken(): string | null {
    const token = localStorage.getItem(TOKEN_KEY);
    const expiry = localStorage.getItem(EXPIRY_KEY);
    if (!token || !expiry) return null;

    // Verifier l'expiration ICI evite de partir en 401 a chaque appel pendant des heures :
    // on sait des le chargement qu'il faut se reconnecter.
    if (new Date(expiry).getTime() <= Date.now()) {
      authService.logout();
      return null;
    }
    return token;
  },

  isAuthenticated(): boolean {
    return authService.getToken() !== null;
  },

  /** Instance axios NUE : le client principal porte l'intercepteur, l'utiliser ici bouclerait. */
  async login(password: string): Promise<void> {
    const { data } = await axios.post<{ success: boolean; data: LoginResult; message?: string }>(
      `${API_BASE}/api/auth/login`,
      { password },
      { headers: { 'Content-Type': 'application/json' }, timeout: 30000 }
    );

    if (!data.success || !data.data?.token) {
      throw new Error(data.message || 'Connexion refusee');
    }

    localStorage.setItem(TOKEN_KEY, data.data.token);
    localStorage.setItem(EXPIRY_KEY, data.data.expiresAt);
  },

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(EXPIRY_KEY);
  },
};
