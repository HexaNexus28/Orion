import ReactDOM from 'react-dom/client'
import App from './App'
import { EntityProvider } from './context/EntityContext'
import { OrionStatusProvider } from './context/OrionStatusContext'
import { HudCardsProvider } from './context/HudCardsContext'
import { AuthGate } from './components/auth/AuthGate'
import './index.css'

/**
 * Service worker : enregistrement ET vérification périodique des mises à jour.
 *
 * Le SW était bien enregistré, mais RIEN ne vérifiait jamais qu'une nouvelle version existait.
 * En `display: standalone`, l'application ne recharge pas la page : gardée ouverte, elle peut
 * rester sur un bundle périmé INDÉFINIMENT.
 *
 * Constaté en production le 2026-08-26 : le téléphone appelait /ws/voice sans aucun jeton et
 * retentait toutes les 3 s — deux signatures du code antérieur, alors que le serveur servait
 * déjà la bonne version. Aucune erreur nulle part, juste un ORION muet.
 */
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  // Y avait-il déjà un contrôleur AVANT cet enregistrement ? Au tout premier chargement il
  // n'y en a pas, et `controllerchange` se déclenche quand même : recharger là créerait un
  // rechargement inutile à chaque première visite.
  const avaitUnControleur = !!navigator.serviceWorker.controller;

  navigator.serviceWorker.register('/sw.js')
    .then((registration) => {
      // Contrôle périodique, pour une session laissée ouverte des heures.
      setInterval(() => { void registration.update(); }, 15 * 60 * 1000);

      // LE contrôle qui compte : au retour au premier plan. Une PWA passe son temps en
      // arrière-plan et revient sans jamais recharger — c'est exactement le moment où elle
      // doit se demander si elle est encore à jour.
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') void registration.update();
      });
    })
    .catch((error) => {
      console.warn('[SW] Enregistrement impossible :', error);
    });

  // `autoUpdate` active le nouveau SW dès son installation (skipWaiting + clientsClaim), mais
  // la PAGE continue de faire tourner l'ancien bundle jusqu'à un rechargement.
  let rechargementEnCours = false;
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (!avaitUnControleur || rechargementEnCours) return;
    rechargementEnCours = true;
    window.location.reload();
  });
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <AuthGate>
    <EntityProvider>
      <OrionStatusProvider>
        <HudCardsProvider>
          <App />
        </HudCardsProvider>
      </OrionStatusProvider>
    </EntityProvider>
  </AuthGate>,
)
