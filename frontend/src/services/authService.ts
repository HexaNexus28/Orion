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

/**
 * En-tetes d authentification pour un appel fetch().
 *
 * fetch() est OBLIGATOIRE partout ou l on lit un ReadableStream (chat en flux, voix binaire) :
 * axios ne sait pas le faire. Mais ces appels echappent donc a l intercepteur d apiClient, et
 * chaque site d appel doit poser le jeton LUI-MEME. Le centraliser ici evite qu un nouvel appel
 * parte sans Authorization — c est exactement ce qui avait rendu le chat muet sur telephone.
 */
export function fetchAuthHeaders(extra: Record<string, string> = {}): Record<string, string> {
  const headers: Record<string, string> = { ...extra };
  const token = authService.getToken();
  if (token) headers.Authorization = `Bearer ${token}`;
  return headers;
}

/**
 * Garde de session pour une reponse fetch(). Leve si la session est invalide.
 *
 * Meme role que l intercepteur d apiClient, pour les appels qui ne passent pas par lui. Sans
 * lui, un jeton perime laisse l interface afficher "Stream failed: 401" indefiniment, sans
 * jamais proposer de se reconnecter.
 *
 * 403 compte AUSSI : un jeton emis avant l ajout du role `owner` est refuse ainsi, et le front
 * n appelle aucune route reservee au daemon — un 403 ne peut donc venir que de la session.
 */
export function assertSessionValide(response: Response): void {
  if (response.status === 401 || response.status === 403) {
    authService.logout();
    window.dispatchEvent(new CustomEvent("orion:unauthenticated"));
    throw new Error("Session expiree — reconnexion necessaire");
  }
}
