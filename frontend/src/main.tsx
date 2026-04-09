import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import { EntityProvider } from './context/EntityContext'
import { OrionStatusProvider } from './context/OrionStatusContext'
import './index.css'

// Register Service Worker for PWA
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js')
    .then((registration) => {
      console.log('SW registered:', registration);
    })
    .catch((error) => {
      console.log('SW registration failed:', error);
    });
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <EntityProvider>
      <OrionStatusProvider>
        <App />
      </OrionStatusProvider>
    </EntityProvider>
  </React.StrictMode>,
)
