# Roadmap — ORION (état)

> ⚠️ **Réordonnée.** Les phases 1-5 ci-dessous ont été construites *en largeur* avant
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
- **J6a ✅ File d'actions différées** — `IToolInvoker` devient le point d'application
  unique de l'exécution d'outil (les 13 gardes `IsConnected` recopiés dans les outils ont disparu),
  `ITool.IsDeferrable`, table `deferred_actions` + TTL 24 h, drain à la reconnexion du daemon,
  confirmation des actions destructives **au réveil** sur l'état réel de la machine, file visible et
  annulable dans la PWA. Prouvé en exécution réelle : Notepad ouvert au réveil, fichier écrit après
  confirmation, lecture refusée franchement. 21 tests ajoutés (222 au total).
- **J6b ✅ Embedding distant** — `OpenAiCompatibleEmbeddingService`, **mistral-embed
  1024 dims**, Ollama RETIRÉ du chemin de production. Le choix est parti d'une MESURE : les API ont
  été appelées une par une (le catalogue ment — 410 Gone, 404, dimensions non indexables). Modèle et
  dimension stockés à côté de chaque vecteur et vérifiés au démarrage, plus `MemoryRevectorizer`
  pour rejouer la table. Voir ADR-013.
- **J6c ✅ Déploiement 24/7** — backend sur VPS derrière la façade Nginx (port loopback), base
  PostgreSQL Cloud, PWA servie par le backend depuis `wwwroot`.
- **J7 ✅ Authentification** — modèle unique : fermée par défaut (`FallbackPolicy`),
  fail-closed sur secret absent, billet de flux à audience distincte pour SSE/WebSocket,
  origines WebSocket alignées sur le CORS. Voir ADR-016.
- **J8 ✅ Garde-fou des actions destructives** — déplacé du prompt système vers le
  CODE : toute action `IsDestructive` passe par la file de confirmation, PC allumé compris.
  Voir ADR-015.
- **J9 ✅ HUD à zones + contexte de travail** — widgets permanents, `get_work_context`.
- **J10 ✅ Voix : Voxtral + Silero** — transcription distante (5,0 s → 0,35 s), repli
  Whisper local, détection de parole par modèle Silero v5.

## Sécurité — chantier ouvert

**J11 ✅ Périmètre des outils** — les sept constats de l'audit sont fermés.
Voir [security.md](security.md).

- **C1 + C2** — `PathScope` (`Orion.Daemon.Core/Security`) : `read_file`, `list_files` et
  `write_file` portent un périmètre, fail-closed, appliqué sur le chemin normalisé, liens résolus,
  noms sensibles refusés même sous une racine autorisée, filtrage appliqué aussi au listing.
  Périmètre d'écriture distinct (`AllowedWriteRoots`) — lire et écrire ne sont pas la même
  permission. `DaemonOptions` est enfin **lu**.
- **E1** — `run_script` en `-EncodedCommand` : ce qui s'exécute est exactement ce qui a été
  confirmé. Plus `-NoProfile`, `-NonInteractive`, un plafond de durée et la lecture des flux avant
  l'attente (deux blocages possibles du daemon, hors audit initial).
- **E2** — `UrlScope` (`Orion.Business/Tools/Internet`) : schéma fermé, adresses internes refusées
  **après résolution DNS**, `BlockedDomains` enfin lu, redirections revalidées saut par saut,
  navigations Playwright filtrées. Le garde ad-hoc de `screenshot_page` (une liste de sous-chaînes
  qui bloquait « login » et laissait passer `169.254.169.254`) a disparu.
- **M1** — `kill_process` refuse et énumère au lieu de tuer N processus pour un nom.
- **M2** — triple lecture disque de `ReadFileAction`.
- **M3** — la doc n'enseigne plus `-ExecutionPolicy Bypass`.

Reste ouvert et documenté : DNS rebinding, sous-ressources de `web_browse`, et le fait que le
périmètre vive côté daemon (donc connu du modèle seulement après l'aller-retour).

**J12 ✅ Frein sur `/api/auth/login`** — seuls
les **échecs** sont comptés, et le mot de passe est vérifié **avant** le frein : la devinette est
plafonnée (5 essais / 15 min) sans que le propriétaire puisse être enfermé dehors. Un limiteur
par IP aurait été du théâtre — sans `UseForwardedHeaders`, `RemoteIpAddress` vaut le proxy pour
toutes les requêtes. Le débit brut reste du ressort de `limit_req` côté Nginx.

**J13 ✅ Chaîne de mise à jour du daemon** — ORION a deux chaînes de déploiement asymétriques :
le backend se déploie seul (Actions → ghcr → `deploy-orion`), le daemon est compilé depuis le
source **local**. Sans signal, le binaire de la machine dérive du dépôt sans que rien ne l'indique.

Trois pièces : `install-daemon.ps1` annonce le périmètre disque appliqué et refuse le silence
quand il est vide ; le crochet `.githooks/post-merge` signale tout `git pull` touchant `daemon/` ;
le périmètre de production est renseigné. Reste à faire : qu'ORION le signale lui-même —
`WorkWatcher` compte déjà les commits non poussés, le miroir est à écrire.

## Voix — chantier ouvert

**V1 ✅ Isolation de l'écho dans la boucle vocale** — le VAD tourne volontairement pendant
qu'ORION parle, pour permettre le barge-in. Sans garde à l'émission, sa propre voix captée par le
micro repart au serveur et se colle devant la phrase suivante : le transcripteur reçoit un
**collage** au lieu d'une phrase, et le modèle répond à ses propres mots.

Une garde qui empêche de **démarrer un tour** ne suffit pas — il faut empêcher d'**émettre**.
D'où l'invariant : **une prise = un tour = un envoi**, l'audio partant collé au `end_audio` qui le
consomme, depuis un seul endroit. Les prises nées pendant qu'ORION parle sans barge-in déclaré
sont écartées, ce qui ferme la fenêtre résiduelle de la *queue* de sa réponse.

L'annulation d'écho du navigateur ne pouvait pas y suffire : MicVAD demande bien
`echoCancellation`, mais un navigateur n'annule que ce **qu'il joue lui-même** — or les réponses
passent par `speechSynthesis`, donc par le moteur TTS du système.

⏸ **À valider à la voix, en conditions réelles** : c'est le seul juge.

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
