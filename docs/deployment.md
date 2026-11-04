# Déploiement

## Topologie

```
Navigateur ── HTTPS ──► Nginx ── HTTP local ──► Backend (conteneur)
Daemon ───── WSS /daemon ─────────────────────► Backend
Backend ──────────────────────────────────────► PostgreSQL + pgvector
```

Le backend sert aussi la PWA depuis `wwwroot`.

## Configuration

`appsettings.json` et `appsettings.Development.json` ne sont pas versionnés. En production, les
valeurs arrivent par variables d'environnement : `Auth__JwtSecret` correspond à la clé `JwtSecret`
de la section `Auth`. Le modèle complet est dans [`.env.example`](../.env.example).

### Valeurs obligatoires

| Variable | Si elle est absente |
|---|---|
| `Auth__Password`, `Auth__JwtSecret` | `/api/auth/login` répond 503 |
| `DAEMON_WS_TOKEN` | connexion du daemon refusée |
| `AllowedOrigins__0` | refus de démarrer |
| `ConnectionStrings__Supabase` | refus de démarrer |

`AllowedOrigins` alimente à la fois le CORS et les origines WebSocket.

### Autres variables

| Groupe | Variables |
|---|---|
| LLM distant | `Nim__ApiKey`, `Nim__Model`, `Nim__BaseUrl` |
| LLM local | `Ollama__BaseUrl`, `Ollama__Model`, `Ollama__NumCtx` |
| Embeddings | `Embedding__ApiKey`, `Embedding__Model`, `Embedding__Dimensions` |
| Transcription | `Transcription__ApiKey`, `Transcription__BaseUrl`, `Transcription__TimeoutSeconds` |
| Synthèse vocale | `Speech__ApiKey`, `Speech__Model`, `Speech__TimeoutSeconds`, `Speech__Enabled` |
| Recherche web | `Internet__SearchApiProvider`, `Internet__BraveApiKey`, `Internet__SerpApiKey` |

`Transcription__ApiKey` et `Speech__ApiKey` reprennent `Embedding__ApiKey` si elles sont vides.
`Speech__Enabled=false` désactive Voxtral TTS au profit de Kokoro.

`Ollama__NumCtx` est obligatoire si Ollama est utilisé.

`Embedding__Dimensions` doit correspondre à la colonne `memory_vectors.embedding`.

## Nginx

- Propager `Upgrade` et `Connection` sur `/daemon` et `/ws/voice`.
- Masquer `access_token` dans les logs d'accès.

Les middlewares WebSocket sont montés avant `UseHttpsRedirection`, car l'application reçoit du HTTP
local derrière Nginx. La journalisation des URL complètes est désactivée en production, car elles
contiennent le billet de flux.

## Base de données

```bash
psql "$CONNECTION_STRING" -f memory/schema.sql
```

Appliquer ensuite `memory/migrations/` dans l'ordre.

## Daemon

Voir [daemon.md](daemon.md). `Daemon:Token` doit être identique à `DAEMON_WS_TOKEN`.

## Développement local

```bash
cp .env.example .env
dotnet run --project backend/Orion.Api       # http://localhost:5107
dotnet run --project daemon/Orion.Daemon
cd frontend && npm run dev                   # http://localhost:3000
```

En développement, Swagger est disponible sur `/swagger` et les origines locales sont autorisées.

## Intégration continue

| Workflow | Déclencheur | Rôle |
|---|---|---|
| `ci.yml` | pull request, push sur `main` | compile et teste le backend et le daemon, construit le frontend |
| `build-orion-api.yml` | push sur `main` touchant `backend/` ou `frontend/` | construit l'image, la publie sur GHCR et déclenche le déploiement |

Le déploiement utilise une clé SSH limitée côté serveur à une commande de redéploiement.

## Vérification

```bash
curl -s https://example.com/health
docker logs orion --since 2m
```

`/health` est public et n'expose aucune donnée. Au démarrage, les logs indiquent le modèle LLM élu.
