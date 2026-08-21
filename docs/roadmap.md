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
  ⚠️ Livré sur embeddings **locaux** — dette ouverte, voir J6b.
- **J5 ✅ Proactivité** — watchers daemon → scoring d'urgence → prise de parole, 5 étages.
- **J6a ✅ File d'actions différées** (2026-08-21) — `IToolInvoker` devient le point d'application
  unique de l'exécution d'outil (les 13 gardes `IsConnected` recopiés dans les outils ont disparu),
  `ITool.IsDeferrable`, table `deferred_actions` + TTL 24 h, drain à la reconnexion du daemon,
  confirmation des actions destructives **au réveil** sur l'état réel de la machine, file visible et
  annulable dans la PWA. Prouvé en exécution réelle : Notepad ouvert au réveil, fichier écrit après
  confirmation, lecture refusée franchement. 21 tests ajoutés (222 au total).
- **J6b ⛔ Embedding distant** — **bloquant du déploiement** : `EmbeddingService` appelle Ollama en
  local, absent du VPS ; `memory_vectors.embedding` est `vector(768)` et n'est plus vide.
  Commence par une mesure (volume réel, catalogue NIM et sa dimension), pas par un choix.
- **J6c Déploiement 24/7** — backend sur VPS derrière la façade Nginx (port loopback), base Supabase
  Cloud, PWA branchée dessus. Dépend de J6b.

## Phases historiques

- **Phase 1** Core MVP : LLMRouter + ConversationAgent + RAG + Tools ShiftStar + entité SSE + briefing
- **Phase 2** Daemon : Worker Service + Watchers + Notifiers + WS sécurisé backend ↔ daemon
- **Phase 3** Connecteurs : Gmail/Calendar + web_search/fetch/browse (Playwright) + tools mémoire autonomes
- **Phase 4 ✅** Voix : Whisper STT · Kokoro TTS · VAD · WS `/ws/voice` full-duplex · barge-in · anti-écho
- **Phase 4.5 ✅** Latence voix : prompt voix dédié · smart split · TTS pipeliné · pre-decode · binary WS
- **Phase 5 🚧** 3D holographique : Scene3D + HologramCard/Chart/ResponsePanel ✅ ·
  entité ORION migrée en Three.js (en cours, actuellement Canvas 2D) · MediaPipe gestes mains
- **Phase 6** Intelligence proactive : screen awareness (vision) · context switching · smart
  interruptions (scoring urgence) · briefing adaptatif · learning loop · ambient mode · multi-session memory
- **Phase 7** Capacités Jarvis : code review auto · deploy monitoring · task execution autonome ·
  multi-tool chaining · voice commands système · email/calendar · Playwright automation
- **Phase 8** HexaNexus : widget dashboard · auth unifiée · moteur IA multi-tenant
