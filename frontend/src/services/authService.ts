import axios from 'axios';
import { API_BASE } from '../config/endpoints';

/**
 * Authentification d'ORION : mot de passe SAISI, echange contre un jeton a duree limitee. Rien
 * de permanent ne vit cote client — un bundle navigateur est lisible par tous.
 *
 * localStorage et non sessionStorage : sur telephone l'app est fermee sans cesse, et retaper le
 * mot de passe dix fois par jour pousse a en choisir un faible.
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

  /**
   * Billet de flux — 60 s, seul jeton autorise dans une URL (SSE et WebSocket navigateur ne
   * portent pas d en-tete, et une URL finit dans les journaux).
   *
   * A redemander a CHAQUE reconnexion : un billet expire ne rouvre rien.
   * Axios NU comme login() : apiClient ferait boucler l intercepteur sur un 401.
   */
  async getStreamTicket(): Promise<string> {
    const token = authService.getToken();
    if (!token) throw new Error("Session absente");

    const { data } = await axios.post<{ success: boolean; data: LoginResult; message?: string }>(
      `${API_BASE}/api/auth/stream-ticket`,
      {},
      { headers: { Authorization: `Bearer ${token}` }, timeout: 15000 }
    );

    if (!data.success || !data.data?.token) {
      throw new Error(data.message || "Billet de flux refuse");
    }
    return data.data.token;
  },

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(EXPIRY_KEY);
  },
};

/**
 * En-tetes d authentification pour fetch(), obligatoire des qu on lit un ReadableStream. Ces
 * appels echappent a l intercepteur d apiClient : centraliser ici evite qu un nouveau site
 * d appel parte sans Authorization.
 */
export function fetchAuthHeaders(extra: Record<string, string> = {}): Record<string, string> {
  const headers: Record<string, string> = { ...extra };
  const token = authService.getToken();
  if (token) headers.Authorization = `Bearer ${token}`;
  return headers;
}

/**
 * Garde de session pour une reponse fetch() — l equivalent de l intercepteur d apiClient pour
 * les appels qui ne passent pas par lui.
 *
 * 403 compte AUSSI : le front n appelle aucune route reservee au daemon, un 403 ne peut donc
 * venir que de la session.
 */
export function assertValidSession(response: Response): void {
  if (response.status === 401 || response.status === 403) {
    authService.logout();
    window.dispatchEvent(new CustomEvent("orion:unauthenticated"));
    throw new Error("Session expiree — reconnexion necessaire");
  }
}
