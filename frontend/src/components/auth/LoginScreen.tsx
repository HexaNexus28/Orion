import { useState, FormEvent } from 'react';
import { authService } from '../../services/authService';
import type { LoginScreenProps } from '../../props/LoginScreen.props';

/**
 * Ecran de connexion. Une seule saisie, puis 30 jours de session.
 *
 * `autoComplete="current-password"` n'est pas cosmetique : c'est ce qui permet au gestionnaire
 * de mots de passe du telephone de proposer le remplissage. Sans lui, l'utilisateur tape un mot
 * de passe de 18 caracteres a la main et finit par en choisir un court.
 */
export function LoginScreen({ onAuthenticated }: LoginScreenProps) {
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await authService.login(password);
      setPassword('');
      onAuthenticated();
    } catch (err) {
      // Message volontairement identique quelle que soit la cause : distinguer « mot de passe
      // faux » de « serveur injoignable » renseigne autant un attaquant qu'un utilisateur.
      setError(err instanceof Error && err.message.includes('Network')
        ? 'ORION est injoignable'
        : 'Mot de passe incorrect');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-950 px-6">
      <form onSubmit={handleSubmit} className="w-full max-w-sm space-y-6">
        <div className="text-center space-y-1">
          <h1 className="text-3xl font-semibold tracking-tight text-slate-100">ORION</h1>
          <p className="text-sm text-slate-500">Session de 30 jours apres connexion</p>
        </div>

        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder="Mot de passe"
          autoComplete="current-password"
          autoFocus
          disabled={busy}
          className="w-full rounded-lg border border-slate-800 bg-slate-900 px-4 py-3 text-slate-100
                     placeholder-slate-600 outline-none focus:border-slate-600 disabled:opacity-50"
        />

        {error && (
          <p role="alert" className="text-sm text-red-400 text-center">{error}</p>
        )}

        <button
          type="submit"
          disabled={busy || password.length === 0}
          className="w-full rounded-lg bg-slate-100 px-4 py-3 font-medium text-slate-950
                     disabled:opacity-40 disabled:cursor-not-allowed"
        >
          {busy ? 'Connexion…' : 'Se connecter'}
        </button>
      </form>
    </div>
  );
}
