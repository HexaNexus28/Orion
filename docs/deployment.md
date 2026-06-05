# Déploiement & Dev Local — ORION

## Backend (Render)

- Service : Web Service · Runtime : Docker · Health : `GET /health`
- WebSocket : Render free tier supporte WSS natif (`wss://orion-api.onrender.com/daemon` + `/ws/voice`)
- Env :
  ```
  ASPNETCORE_ENVIRONMENT=Production
  SUPABASE_URL=  SUPABASE_SERVICE_KEY=
  ANTHROPIC_API_KEY=  OLLAMA_URL=
  DAEMON_WS_TOKEN=  JWT_SECRET=
  ```

## Frontend (Vercel)

- Framework : Vite · Build : `npm run build` → `dist/` · PWA : Service Worker auto (vite-plugin-pwa)
- Env : `VITE_API_URL=https://orion-api.onrender.com` · `VITE_WS_URL=wss://orion-api.onrender.com`

## Daemon (machine Windows)

Cf. [daemon.md](daemon.md). `appsettings.json` → `RenderWsUrl: wss://orion-api.onrender.com/daemon`.

## Dev Local

```bash
cd backend  && dotnet run --project Orion.Api      # http://localhost:5107
cd daemon   && dotnet run --project Orion.Daemon
cd frontend && npm run dev                          # http://localhost:5173
# Ollama = service Windows déjà actif (http://localhost:11434)
```

En dev, `appsettings.Development.json` ne surcharge que `Ollama.BaseUrl` → `Model`/`FallbackModel`
viennent de `appsettings.json` (vérifier qu'ils existent dans `ollama list`).

## .env.example

```env
SUPABASE_URL=https://xxx.supabase.co
SUPABASE_SERVICE_KEY=eyJ...
ANTHROPIC_API_KEY=sk-ant-...
OLLAMA_BASE_URL=http://localhost:11434
OLLAMA_MODEL=deepseek-v4-flash:cloud
DAEMON_WS_URL=ws://localhost:5107/daemon
DAEMON_WS_TOKEN=secret-token-orion
JWT_SECRET=orion-jwt-secret-change-this
VITE_API_URL=http://localhost:5107
VITE_WS_URL=ws://localhost:5107
```
