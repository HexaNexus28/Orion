# Déploiement & Dev Local — ORION

> ⚠️ **Réécrit le 2026-08-27.** Cette page décrivait un déploiement Render + Vercel qui n'est plus
> celui du projet. L'hébergement est un **VPS unique derrière Nginx**, et la PWA est servie **par
> le backend**. Les noms `RenderWsUrl` / `orion-api.onrender.com` sont des vestiges.

## Topologie réelle

```
Navigateur ──HTTPS──►  Nginx (TLS, façade)  ──HTTP loopback──►  Backend .NET (conteneur)
                                                                    │
Daemon Windows ──WSS /daemon (X-Daemon-Token)──────────────────────►│  (le daemon INITIE)
                                                                    ▼
                                                   PostgreSQL + pgvector (Supabase Cloud)
```

La PWA **n'est pas hébergée séparément** : le bundle construit vit dans `wwwroot` et le backend le
sert lui-même (cf. `Dockerfile`), avant l'authentification — la coquille de l'application n'est pas
un secret, et si elle exigeait une session, l'utilisateur n'aurait jamais l'écran pour en ouvrir une.

## Configuration — d'où viennent les valeurs

⚠️ **`appsettings.json` et `appsettings.Development.json` sont gitignorés, donc ABSENTS de l'image.**
En production, tout arrive par **variables d'environnement**, posées par Ansible. Le double
souligné correspond à l'imbrication .NET : `Auth__JwtSecret` → section `Auth`, clé `JwtSecret`.

Modèle complet et commenté : [`.env.example`](../.env.example) à la racine.

### Les valeurs fail-closed — absentes, ça REFUSE

Ce ne sont pas des pannes, c'est le comportement voulu. Un secret absent ne doit jamais ouvrir.

| Variable | Effet si absente |
|---|---|
| `Auth__Password`, `Auth__JwtSecret` | `/api/auth/login` répond **503** — aucune session possible |
| `DAEMON_WS_TOKEN` | WebSocket daemon **refusé** (avant, un jeton non configuré faisait sauter le contrôle) |
| `AllowedOrigins__0` | **refus de démarrer** — liste vide = le framework accepte TOUTE origine, donc détournement de WebSocket inter-sites |
| `ConnectionStrings__Supabase` | **refus de démarrer** |

`AllowedOrigins` est la **source de vérité unique** du CORS *et* des origines WebSocket : deux
listes d'origines finissent toujours par diverger. En production la valeur vient du domaine déclaré
de la stack — depuis un téléphone, le navigateur envoie l'origine publique, pas `localhost`.

### Les autres

| Groupe | Variables |
|---|---|
| Cerveau distant | `Nim__ApiKey`, `Nim__Model`, `Nim__BaseUrl` |
| Cerveau local (repli) | `Ollama__BaseUrl`, `Ollama__Model`, **`Ollama__NumCtx`** |
| Embeddings | `Embedding__ApiKey`, `Embedding__Model`, `Embedding__Dimensions` |
| Transcription | `Transcription__ApiKey` (retombe sur celle des embeddings si vide) |
| Recherche web | `Internet__SearchApiProvider`, `Internet__BraveApiKey`, `Internet__SerpApiKey` |

⚠️ `Ollama__NumCtx` est **obligatoire** : sans elle Ollama dimensionne le cache KV sur le contexte
maximum du modèle (128k) et réclame ~15 Go pour un modèle de 2 Go → HTTP 500 intermittents.

⚠️ `Embedding__Dimensions` doit correspondre **exactement** à la colonne `memory_vectors.embedding`.
Changer de modèle d'embedding impose de revectoriser toute la table (`POST /api/memory/revectorize`) :
mélanger deux espaces vectoriels ne lève aucune erreur et renvoie des résultats absurdes.

## Nginx — points qui cassent si on les oublie

- **Upgrade WebSocket** à propager sur `/daemon` et `/ws/voice` (`Upgrade` / `Connection`).
- **Ne pas** laisser `UseHttpsRedirection` intercepter l'upgrade : derrière Nginx, l'application
  voit du HTTP en loopback. C'est pourquoi les middlewares WebSocket sont montés **avant** dans
  `Program.cs` — les descendre casserait les deux canaux (redirection 307).
- **Masquer le jeton dans les journaux** : Nginx remplace `access_token` par `***`. ASP.NET
  journalise l'URL complète, donc cette copie non masquée est coupée en production
  (`Microsoft.AspNetCore.Hosting.Diagnostics` → `Warning`). Constaté le 2026-08-26 : un billet en
  clair dans `access.log`.

## Base de données

```bash
psql "$CONNECTION_STRING" -f memory/schema.sql
psql "$CONNECTION_STRING" -f memory/seed.sql
```

pgvector est requis. La dimension d'index plafonne à 2000 — d'où le choix d'un modèle à 1024 dims.

## Daemon (machine Windows)

```powershell
powershell -File scripts/install-daemon.ps1
```

Lancé par le **dossier Démarrage**, pas en service Windows (un service vit en session 0, isolée du
bureau : ORION serait muet et invisible). Détail et pièges : [daemon.md](daemon.md).

Configuration hors dépôt : `%LOCALAPPDATA%\Orion\daemon\appsettings.Production.json`. Le champ
`Daemon:Token` doit être **identique** à `DAEMON_WS_TOKEN` côté serveur.

## Dev local

```bash
cp .env.example .env                              # puis renseigner
dotnet run --project backend/Orion.Api            # http://localhost:5107
dotnet run --project daemon/Orion.Daemon
cd frontend && npm run dev                        # http://localhost:5173
```

En développement : Swagger est exposé sur `/swagger`, la politique CORS `DevelopmentPolicy`
s'applique, et les origines `localhost:5173` / `localhost:3000` sont ajoutées aux origines
WebSocket — **uniquement** en développement, où elles n'élargissent la surface de rien.

## Intégration continue

| Workflow | Déclencheur | Ce qu'il garde |
|---|---|---|
| `ci.yml` | pull request · push sur `main` | compile backend **et daemon**, exécute les deux suites de tests, `tsc` + build du front |
| `build-orion-api.yml` | push sur `main` (`backend/**`, `frontend/**`) | construit l'image, la pousse sur GHCR, puis déclenche le déploiement |

⚠️ `build-orion-api.yml` ne compile que `Orion.Api` — donc **ni le daemon, ni les tests** — et
seulement **après** le merge. Le 2026-08-31, une erreur de compilation est ainsi passée sur `main` :
l'échec n'est apparu qu'au build d'image, et l'image n'a pas été publiée — le VPS est resté sur la
version précédente. C'est ce trou que `ci.yml` referme, en se déclenchant **sur la pull request**.

Le déploiement suit la publication de l'image. La clé SSH est **verrouillée côté serveur** sur
`/usr/local/bin/deploy-orion` (`restrict,command=` dans `authorized_keys`) : la commande envoyée
par le workflow est ignorée, le serveur exécute toujours son script. Une fuite de `VPS_SSH_KEY` ne
vaut que le droit de redéployer ORION — ni shell, ni docker, ni lecture de fichiers.

## Vérifier que ça tourne

```bash
curl -s https://<domaine>/health                     # sonde ouverte, n'expose aucune donnée
docker logs orion --since 2m | grep -E "Daemon connected|Tool registered|probe"
```

Au démarrage, la **sonde LLM** appelle réellement le fournisseur pour élire le modèle servi :
`ollama list` ne prouve rien (7 modèles listés, 7 inutilisables — 2026-08-20). Sans cette sonde,
la panne est invisible et ORION bascule en silence sur un modèle dégradé.

## Sécurité

Avant d'exposer une instance, lire **[security.md](security.md)** : l'audit du 2026-08-27 documente
des constats **ouverts**, dont deux critiques sur le périmètre des outils de fichiers.
