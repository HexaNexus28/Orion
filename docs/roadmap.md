# Roadmap — ORION (état)

> ⚠️ **Réordonnée le 2026-08-20.** Les phases 1-5 ci-dessous ont été construites *en largeur* avant
> qu'ORION ait un cerveau : le chemin réellement utilisé par l'UI n'exécutait aucun outil.
> Voir [jarvis-gap-analysis.md](jarvis-gap-analysis.md). L'agentivité est la **fondation**, pas la
> phase 7 — le plan Jarvis ci-dessous prime sur l'ordre historique.

## Plan Jarvis (prioritaire)

- **J1 ✅ Boucle agent** — `AgentLoop` multi-tours, outils branchés en streaming, événements typés
  (`token`/`tool_start`/`tool_result`/`done`/`error`) jusqu'à l'UI. Sonde LLM au démarrage.
  44 tests verts dont 4 d'intégration contre Ollama réel.
- **J2 ✅ Prompts** — prompt agent réécrit : quand agir / quand s'abstenir, chaînage, garde-fous
  sur les outils destructifs, vraie liste d'outils injectée avec descriptions et métadonnées
  (`ITool.RequiresDaemon` / `ITool.IsDestructive`), prompt voix distinct. Sections ordonnées
  **stable → volatil** pour le cache de préfixe. **Filtrage : un outil qui ne peut pas aboutir
  n'est plus proposé au modèle** — affiné en J6a : le tri se fait par *utilité différée*, pas par
  disponibilité. 15 tests de prompt.
- **J3 ✅ Cerveau** — client OpenAI-compatible (NVIDIA NIM), cascade explicite NIM → local.
- **J4 ✅ Mémoire** — écriture auto à chaque tour, consolidation, schéma fermé 4 slots, garde-fous.
  ⚠️ Livré sur embeddings **locaux** — dette RÉSORBÉE par J6b (mistral-embed distant).
- **J5 ✅ Proactivité** — watchers daemon → scoring d'urgence → prise de parole, 5 étages.
- **J6a ✅ File d'actions différées** (2026-08-21) — `IToolInvoker` devient le point d'application
  unique de l'exécution d'outil (les 13 gardes `IsConnected` recopiés dans les outils ont disparu),
  `ITool.IsDeferrable`, table `deferred_actions` + TTL 24 h, drain à la reconnexion du daemon,
  confirmation des actions destructives **au réveil** sur l'état réel de la machine, file visible et
  annulable dans la PWA. Prouvé en exécution réelle : Notepad ouvert au réveil, fichier écrit après
  confirmation, lecture refusée franchement. 21 tests ajoutés (222 au total).
- **J6b ✅ Embedding distant** (2026-08-25) — `OpenAiCompatibleEmbeddingService`, **mistral-embed
  1024 dims**, Ollama RETIRÉ du chemin de production. Le choix est parti d'une MESURE : les API ont
  été appelées une par une (le catalogue ment — 410 Gone, 404, dimensions non indexables). Modèle et
  dimension stockés à côté de chaque vecteur et vérifiés au démarrage, plus `MemoryRevectorizer`
  pour rejouer la table. Voir ADR-013.
- **J6c ✅ Déploiement 24/7** — backend sur VPS derrière la façade Nginx (port loopback), base
  PostgreSQL Cloud, PWA servie par le backend depuis `wwwroot`.
- **J7 ✅ Authentification** (2026-08-26) — l'API était TOTALEMENT ouverte. Fermée par défaut
  (`FallbackPolicy`), fail-closed sur secret absent, billet de flux à audience distincte pour
  SSE/WebSocket, origines WebSocket alignées sur le CORS. Voir ADR-016.
- **J8 ✅ Garde-fou des actions destructives** (2026-08-26) — déplacé du prompt système vers le
  CODE : toute action `IsDestructive` passe par la file de confirmation, PC allumé compris.
  Voir ADR-015.
- **J9 ✅ HUD à zones + contexte de travail** — widgets permanents, `get_work_context`.
- **J10 ✅ Voix : Voxtral + Silero** (2026-08-27) — transcription distante (5,0 s → 0,35 s), repli
  Whisper local, détection de parole par modèle Silero v5.

## Sécurité — chantier ouvert

**J11 🚧 Périmètre des outils** — audit du 2026-08-27, [security.md](security.md).

- **C1 ✅** (2026-08-27) — `PathScope` dans `Orion.Daemon.Core/Security`. `read_file` et
  `list_files` portent un périmètre `AllowedRoots`, fail-closed, appliqué sur le chemin normalisé,
  liens résolus, noms sensibles refusés même sous une racine autorisée, filtrage appliqué aussi au
  listing. 16 tests écrits sur les contournements. `DaemonOptions` est enfin **lu**.
  M2 corrigé au passage (triple lecture disque).
- **C2 ⛔** — `write_file` écrit encore n'importe où. Le correctif est court : câbler
  `WriteFileAction` sur le `PathScope` existant.
- **E1 ⛔** — `run_script` : passer en `-EncodedCommand`, pour que la commande exécutée soit
  exactement celle qui a été confirmée.
- **E2 ⛔** — `web_fetch` / `web_browse` : schéma, adresses privées, et `BlockedDomains` enfin lu.
- **M1 / M3 ⛔** — `kill_process` annonce N processus ; la doc n'enseigne plus `Bypass`.

Ordre : C2 → E1 → E2 → M1 → M3.

## Phases historiques

- **Phase 1** Core MVP : ConversationAgent + RAG + entité SSE + briefing
  (⚠️ `LLMRouter` remplacé par `LLMCascade` ; les « Tools ShiftStar » n'ont jamais existé)
- **Phase 2** Daemon : Worker Service + Watchers + Notifiers + WS sécurisé backend ↔ daemon
- **Phase 3** Connecteurs : web_search/fetch/browse (Playwright) + tools mémoire autonomes
  (⚠️ Gmail / Calendar : **jamais implémentés**)
- **Phase 4 ✅** Voix : Whisper STT · Kokoro TTS · VAD · WS `/ws/voice` full-duplex · barge-in · anti-écho
- **Phase 4.5 ✅** Latence voix : prompt voix dédié · smart split · TTS pipeliné · pre-decode · binary WS
- **Phase 5 🚧** 3D holographique : Scene3D + HologramCard/Chart/ResponsePanel ✅ ·
  entité ORION migrée en Three.js (en cours, actuellement Canvas 2D) · MediaPipe gestes mains
- **Phase 6** Intelligence proactive : screen awareness (vision) · context switching · smart
  interruptions (scoring urgence) · briefing adaptatif · learning loop · ambient mode · multi-session memory
- **Phase 7** Capacités Jarvis : code review auto · deploy monitoring · task execution autonome ·
  multi-tool chaining · voice commands système · email/calendar · Playwright automation
- **Phase 8** HexaNexus : widget dashboard · auth unifiée · moteur IA multi-tenant
