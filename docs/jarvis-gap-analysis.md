# ORION → Jarvis — Analyse de l'écart

> Document de diagnostic. Établi par lecture du code + builds réels, pas par lecture de la doc.
> Toute affirmation ici est adossée à un `fichier:ligne`.

## 0. Mesures d'entrée (état réel, pas déclaré)

| Mesure | Résultat |
|---|---|
| `dotnet build` backend | ✅ exit 0 |
| `npx tsc --noEmit` frontend | ✅ exit 0 |
| Fichiers source | 136 `.cs` backend · 65 `.ts/.tsx` frontend · 43 `.cs` daemon |
| Modèles configurés présents dans `ollama list` | ✅ oui — mais voir §1.10 : **présent ≠ utilisable** |
| Dernier commit produit | `5cf070b` — 2026-06-05 |
| Tests backend | 6 fichiers (Chat/Health controllers, repos, Audit/Chat/LLM services) |

**Conclusion des mesures** : ORION n'est pas cassé. Il compile, les modèles existent, les 4 couches
sont respectées. Le blocage n'est ni un bug ni une config — il est **structurel**.

---

## 1. Cause racine

> **ORION n'a aucune agentivité dans le seul chemin que l'UI utilise.**
> Ce n'est pas un assistant qui agit. C'est un chatbot habillé en agent.

### Chaîne de preuve

**1.1 — L'UI n'utilise que le streaming.**
`frontend/src/App.tsx:21` → `useStream` (SSE `/api/chat/stream`) et `App.tsx:235` → `useVoiceWS`
(`/ws/voice`). Le endpoint non-streamé `/api/chat` — **le seul qui exécute des tools** — n'est appelé
nulle part dans le flux réel (`useChat.sendMessage` existe mais n'est pas câblé à l'écran principal).

**1.2 — Le chemin streaming ne branche pas d'exécuteur de tools.**
`ConversationAgent.cs:323` construit le `LLMRequest` avec `Tools = toolDefinitions` mais **sans
`ToolExecutor`** — contrairement au chemin non-streamé (`ConversationAgent.cs:172-173`) qui, lui, en a un.

**1.3 — Pire : le client Ollama n'envoie même pas les tools en streaming.**
`OllamaClient.cs:318-323` — le payload de streaming est `{ model, messages, stream: true }`.
**Le champ `tools` n'est pas sérialisé du tout.** Le modèle n'apprend jamais qu'il a des outils.

> **Conséquence** : les 22 tools (`web_search`, `open_app`, `run_script`, `git_commit`,
> `write_file`, `memory_save`…) sont de la décoration dans 100 % des usages réels.
> ORION ne peut **rien** faire. Il peut seulement parler de ce qu'il ferait.

**1.4 — Même le chemin qui marche est mono-coup.**
`OllamaClient.ExecuteToolCallsAndCompleteAsync` (`OllamaClient.cs:165-250`) exécute les tool calls
**une fois**, puis relance le modèle **sans les tools** (`followUpRequest`, ligne ~232).
Il n'y a pas de boucle. Donc « cherche X sur le web *puis* écris le résultat dans un fichier *puis*
commit » est **structurellement impossible**, même via `/api/chat`.
Un Jarvis, c'est exactement ça : du chaînage multi-outils.

**1.5 — La mémoire long terme est morte par construction.**
`memory_vectors` n'est écrit que par `MemoryService.SaveAsync` (`MemoryService.cs:71`), appelé
uniquement par le tool `memory_save` et par `POST /api/memory`. Or les tools ne peuvent pas partir en
streaming (1.2/1.3). Donc la recherche RAG de `ConversationAgent.cs:448` interroge **une table qui
reste vide en permanence**. Aucune conversation n'est vectorisée automatiquement.

**1.6 — Aucune trace d'action remontée à l'UI.**
`ConversationAgent.cs:223-224` : `MemoryUsed = false, // TODO: implement memory` et
`ToolsCalled = null`. L'utilisateur ne peut jamais voir ce qu'ORION a fait.
Pas de trace d'action = pas de confiance = pas de Jarvis.

**1.7 — Le prompt promet des outils qu'il ne liste jamais.**
`PromptBuilder.BuildSystemPrompt` reçoit `availableTools` — passé `new List<ToolCallDto>()` (vide)
aux **deux** points d'appel (`ConversationAgent.cs:118` et `:309`). La section `TOOLS DISPONIBLES`
ne s'affiche donc jamais, alors que le prompt ordonne « Utilise les tools disponibles avant de
répondre ». On demande au modèle d'utiliser une liste vide.

**1.8 — Agents fantômes.**
`IMemoryAgent` et `IToolAgent` sont déclarés dans `Orion.Core/Interfaces/Agents/` avec
**zéro implémentation et zéro référence** (vérifié par grep sur tout `backend/`).
Le pipeline `ConversationAgent → MemoryAgent → ToolAgent` annoncé dans le README **n'existe pas**.

**1.9 — `LLMRouter` ne route rien.**
`LLMRouter.cs:16` : un seul client, Ollama en dur, `throw` s'il manque. Le « fallback Claude »
documenté dans README/CLAUDE.md n'existe pas. Et `OllamaClient.IsAvailable()` (`:36`) fait un
`.Result` — sync-over-async, **violation de la règle R-09 du projet lui-même** — déclenché à
**chaque** requête via `LLMRouter.ActiveProvider`.

---

## 1bis. Deuxième cause racine — le cerveau n'est pas celui que tu crois

La première cause explique pourquoi ORION **n'agit pas**. Celle-ci explique pourquoi ORION
**répond mal**. Elle est indépendante et tout aussi bloquante.

**1.10 — Aucun modèle cloud configuré n'est utilisable. Aucun.**
Test direct sur `POST localhost:11434/api/chat`, un modèle à la fois :

| Modèle (`ollama list` le montre) | Réponse réelle du serveur |
|---|---|
| `deepseek-v4-flash:cloud` ← **primary en prod** | ❌ HTTP **403** — `requires a subscription, upgrade for access` |
| `deepseek-v4-pro:cloud` | ❌ 403 — subscription |
| `qwen3.5:cloud` | ❌ 403 — subscription |
| `deepseek-v3.2:cloud` | ❌ **retiré** le 2026-07-15 |
| `glm-5:cloud` | ❌ **retiré** le 2026-07-15 |
| `kimi-k2.5:cloud` | ❌ **retiré** le 2026-07-31 |
| `minimax-m2.5:cloud` | ❌ **retiré** le 2026-07-31 |
| `llama3.2:3b` (local, 2 Go) | ✅ **seul modèle qui répond** |

> `ollama list` affiche les `:cloud` en cache local même quand ils sont retirés ou verrouillés.
> **La règle « vérifier que le modèle existe dans `ollama list` » est insuffisante — il faut
> l'appeler réellement.** C'est la variante avancée du piège déjà documenté en mémoire projet.

**1.11 — Le fallback masque le problème au lieu de le signaler.**
`OllamaClient.ShouldTryFallback` (`:262`) teste la présence de `"forbidden"` dans le message
d'erreur. Le 403 produit `HttpRequestException: ... 403 (Forbidden)` → le test passe **par
coïncidence de sous-chaîne**, et ORION bascule silencieusement sur `llama3.2:3b`.

Résultat : **depuis des mois, ORION tourne à 100 % sur un modèle 3B**, alors que la config, la doc et
la mémoire projet annoncent un modèle frontier. Chaque tour paie en plus un aller-retour 403 inutile.
Aucun log ne dit « ton modèle principal est inaccessible » — juste un `LogWarning` noyé.

**1.12 — Le matériel ne permet pas de compenser en local.**
Mesuré sur cette machine : **15,7 Go RAM** (2,6 Go libres au moment du test), GPU **Intel Iris Xe
intégré, 2 Go de VRAM partagée** — pas de GPU dédié. Inférence CPU.

Débit réel mesuré, modèle déjà chargé (`llama3.2:3b`) :
**140 tokens en 16,8 s → ≈ 8 tokens/seconde.**

Ce que ça implique, chiffré :

| Usage | Besoin | Réalité mesurée |
|---|---|---|
| Voix (cible doc : 800 ms–1,5 s) | 1ʳᵉ phrase ≈ 30 tokens | **≈ 3,6 s** de génération, **avant** le TTS → cible inatteignable |
| Boucle agent 3 outils | 3 allers-retours LLM | **30–60 s** par demande → inutilisable |
| Raisonnement multi-étapes | modèle ≥ 8B | 3B : sait émettre **un** appel d'outil (vérifié ✅), ne chaîne pas de façon fiable |

**Monter un modèle plus gros en local n'est pas une option — mesuré, pas estimé.**
`llama3.1:8b` téléchargé et exécuté sur cette machine le 2026-08-20 (`num_ctx` 4096) :

| | `llama3.2:3b` | `llama3.1:8b` |
|---|---|---|
| Chargement | 0,7 s | **34,2 s** |
| Débit | 8,3 tokens/s | **1,92 tokens/s** |
| Réponse de 3 phrases | ~17 s | **102 s** |

**4,3× plus lent** — pire que l'estimation prudente de 3 tokens/s. Une seule réponse courte prend
plus d'une minute et demie. Le VPS IONOS (16 Go, sans GPU) ne change rien à ce plafond.

> **Conclusion 1bis** : le cerveau de Jarvis ne peut pas être local sur ce matériel.
> Ce n'est pas un avis, c'est une mesure.

### Bonne nouvelle mesurée

Test direct `tools` + `stream: true` sur `/api/chat` : **Ollama accepte les deux ensemble**, et
`llama3.2:3b` renvoie bien un `tool_calls` correct dans un chunk streamé :

```json
{"message":{"role":"assistant","content":"",
 "tool_calls":[{"function":{"name":"open_app","arguments":{"appName":"Notepad"}}}]},"done":false}
```

→ **Le chantier 1 (`AgentLoop` avec tools en streaming) est techniquement viable dès maintenant, à 0 €,
et testable sur le modèle local.** Rien ne bloque la construction de la boucle. Seule la *qualité*
du raisonnement dépend du choix de cerveau.

---

## 1ter. Découvertes de l'intervention du 2026-08-20

Trois pannes supplémentaires, invisibles jusqu'à ce qu'on exécute réellement le système.

**1.13 — Le streaming n'a jamais streamé.**
L'ancien client appelait `PostAsJsonAsync`, dont l'option par défaut est `ResponseContentRead` :
HttpClient **bufferise la réponse entière** avant de rendre la main. Le « token par token » de la
doc arrivait donc d'un seul bloc à la fin. Corrigé par `HttpCompletionOption.ResponseHeadersRead`,
verrouillé par un test qui exige plus d'un chunk.

**1.14 — Le cache KV dimensionné sur 128k faisait tomber le modèle local.**
Sans `num_ctx` explicite, Ollama alloue le cache KV pour le contexte **maximum** du modèle.
Pour `llama3.2:3b` (128k) cela réclame **15 Go** — pour un modèle de 2 Go. Résultat observé :
`HTTP 500 — failed to allocate buffer for kv cache` dès que la RAM libre baisse.
La panne est **intermittente et dépend de ce qui tourne à côté**, ce qui la rend très difficile à
attribuer. Corrigé : `NumCtx` (8192) désormais obligatoire et envoyé à chaque appel.

**1.15 — La base de données d'ORION n'existe plus.**
`db.niwciampfbwppjpufbnz.supabase.co` → **NXDOMAIN**, API REST injoignable (`status=000`).
Le projet Supabase a disparu — comportement attendu du palier gratuit après une longue inactivité
(dernier commit produit : 2026-06-05). Conséquence : `PrepareStreamAsync` renvoie
`503 Base de donnees inaccessible` et **aucun tour de conversation ne peut aboutir en HTTP**.
C'est le dernier verrou avant le e2e complet — et une décision d'hébergement, pas un bug.

**1.16 — Le daemon renvoyait ses erreurs à une adresse inexistante.**
`DaemonMessageHandler.ProcessMessageAsync` attrapait bien les exceptions, mais répondait avec
`DaemonResponse.ErrorResponse(Guid.NewGuid().ToString(), ...)` — **un identifiant de corrélation
neuf**. Le backend, qui attend l'identifiant de sa requête, ne voyait jamais la réponse et finissait
en timeout : `Failed to send action: A task was canceled.`

Conséquence : **toute** défaillance d'action daemon se présentait comme un timeout opaque, jamais
comme son vrai message d'erreur. Un bug d'une ligne devenait indiagnosticable.
Corrigé : l'identifiant est extrait du JSON brut **avant** toute opération risquée et réutilisé
dans tous les chemins d'erreur.

**1.17 — Deux tools parlaient un autre langage que leur action daemon.**
Les deux moitiés vivent dans des solutions séparées et s'échangent du JSON non typé — rien ne les
empêchait de diverger. Surface cartographiée exhaustivement (16 actions comparées) :

| Tool backend | envoyait | l'action daemon lisait | |
|---|---|---|---|
| `OpenAppTool` | `appName` | `application` | ❌ |
| `ReadFileTool` | `filePath` | `path` | ❌ |
| les 14 autres | — | — | ✅ |

Masqué par §1.16 : le `KeyNotFoundException` partait dans le vide. Corrigé côté backend (le schéma
vu par le LLM garde `appName`/`filePath`, plus parlants ; seule la clé du fil s'aligne), et
verrouillé par `DaemonToolContractTests` — 6 tests qui capturent le payload réellement émis.

**1.18 — Le cache de préfixe décide du coût réel d'un prompt riche.**
Découvert en construisant le chantier 2. Le prompt agent complet (22 schémas d'outils + consignes
d'abstention + garde-fous) pèse **2777 tokens**. Sur `llama3.2:3b` en CPU :

| | prompt jamais vu | préfixe déjà en cache |
|---|---|---|
| Évaluation du prompt | **242,8 s** | **0,4 s** |
| Tour complet | 262,8 s | **8,7 s** |

**Facteur 600.** Les moteurs d'inférence ne réévaluent que ce qui suit le premier octet modifié.
Conséquences appliquées au code :

1. `PromptBuilder` construit désormais **STABLE d'abord** (identité, profil, catalogue d'outils,
   ton) puis **VOLATIL en dernier** (souvenirs RAG, horodatage, état du daemon). L'ordre initial
   plaçait les souvenirs — qui changent à chaque requête — *avant* le catalogue d'outils : chaque
   tour aurait repayé l'évaluation complète.
2. `OllamaOptions.KeepAlive` (30 min) : un déchargement de modèle vide aussi le cache de préfixe.
3. Le timeout HTTP vient de la config (`Ollama:TimeoutSeconds`) au lieu d'une constante de 3 min
   écrite en dur dans `Program.cs` — la bonne valeur diffère d'un facteur 100 entre un CPU local
   et une API distante.

> À retenir : ce n'est **pas** un argument pour appauvrir le prompt. C'est le matériel qui est lent,
> pas le prompt qui est trop riche — sur un GPU distant (NIM), 2777 tokens s'évaluent en dizaines
> de millisecondes. Un argument de plus pour le chantier 3.

---

## 2. Ce que ça dit du produit

ORION a été construit en largeur (voix full-duplex, 3D, daemon, PWA, 22 tools, RAG, briefing)
avant d'avoir un cerveau. Toutes les extrémités sont là — **le boîtier central est vide**.
D'où la sensation de « point mort » : chaque nouvelle feature s'ajoute à un système qui ne peut
toujours pas *agir*.

Un Jarvis tient sur trois piliers. État réel :

| Pilier | Attendu | État ORION |
|---|---|---|
| **Agentivité** — décider et exécuter des actions en chaîne | boucle agent multi-tours | ❌ inexistante dans le chemin utilisé |
| **Mémoire** — se souvenir sans qu'on le lui demande | écriture auto + consolidation | ❌ table vide par construction |
| **Proactivité** — initier sans être sollicité | watchers → scoring → parole | ⚠️ watchers daemon présents, aucune boucle de décision |

Le reste (voix, 3D, PWA, daemon, tools, 4 couches) est **du solide déjà payé**.

---

## 3. Faut-il tout reconstruire ?

**Non — et le « oui » coûterait des semaines pour racheter le même code.**

À garder tel quel (actifs réels, qui buildent) :
squelette 4 couches · 22 tools avec JSON Schema · daemon WS + whitelist d'actions ·
pipeline voix full-duplex (Whisper/Kokoro/VAD/barge-in) · PWA + 3D · repositories + Supabase/pgvector.

À **remplacer** (pas à patcher) — c'est ça, la reconstruction, et elle est chirurgicale :

| Composant | Lignes | Pourquoi remplacer |
|---|---|---|
| `OllamaClient` | 350 | deux chemins divergents (streaming sans tools), boucle mono-coup |
| `LLMRouter` | 46 | ne route rien, `.Result` bloquant |
| `ConversationAgent` | 463 | `ProcessAsync` et `PrepareStreamAsync` dupliquent la logique **et divergent** |
| `PromptBuilder` | 95 | prompt générique, ne liste pas les outils, n'enseigne pas l'action |

**~950 lignes à réécrire sur ~7 000.** Le reste est conservé.

---

## 4. Cible — architecture Jarvis

```
        UI (SSE / WS voix)          ◄── événements typés : token · tool_start · tool_result · done
                 │
        ChatService (ApiResponse)
                 │
   ┌─────────────▼──────────────┐
   │        AgentLoop           │   UN SEUL chemin. Streaming ET non-streaming
   │  while (toolCalls && i<N)  │   passent par lui. Plus de divergence possible.
   │    1. LLM.Stream(tools)    │
   │    2. tool calls ? exec    │
   │    3. réinjecte + reboucle │
   └──┬──────────┬──────────┬───┘
      │          │          │
  ILLMClient  ToolRegistry  MemoryWriter
  (multi-     (22 tools)    (écrit CHAQUE tour,
   provider)                 pas seulement sur demande)
```

**Chantier 0 — le cerveau (décision produit, pas technique).**
`ILLMClient` devient réellement multi-provider et `LLMRouter` route pour de bon : modèle local pour
les tours courts/voix, modèle distant pour les tours agentiques. Le choix du provider distant est
une décision de coût (§1.12) — le reste du chantier est identique quel que soit le choix.
**Garde-fou obligatoire** : au démarrage, ORION *appelle* chaque modèle configuré et refuse de
démarrer en silence si le primaire est inaccessible. Le bug §1.10/1.11 ne doit plus jamais être
invisible.

**Puis 4 chantiers, dans cet ordre — chacun débloque le suivant :**

1. **`AgentLoop`** — un seul point d'application, boucle multi-tours, tools branchés en streaming,
   événements typés vers l'UI. *Sans ça, rien d'autre ne compte.*
2. **Prompts** — réécriture complète : prompt agent (quand agir vs répondre, comment chaîner),
   liste réelle des outils injectée, prompt voix distinct, garde-fous sur les actions destructives.
3. **Mémoire réelle** — écriture automatique + embedding à chaque tour, consolidation, profil vivant.
4. **Proactivité** — watchers daemon → scoring d'urgence → ORION prend la parole.

**Preuve exécutable exigée à chaque chantier** : un test qui échoue si la réalité diverge du modèle.
Ex. chantier 1 : « ORION doit ouvrir Notepad depuis le chat streamé » — test e2e qui rougit tant que
la boucle n'est pas branchée.

---

## 5. Dette annexe relevée (hors chemin critique)

- `orionfix.md` / `frontfix.md` (32 Ko) : briefs Windsurf/Kimi périmés (Kimi K2 Moonshot en primary,
  `Task.Delay(50)` de faux streaming). **Contredisent le code actuel** → à supprimer, pas à maintenir.
- `docs/roadmap.md` : Phase 7 « Capacités Jarvis » listée après la 3D et la vision. **Inversion de
  priorité** — l'agentivité est la fondation, pas la cerise.
- README annonce « fallback Claude », « ConversationAgent → MemoryAgent → ToolAgent », « 33 tests » :
  les trois sont faux. Doc à réaligner sur le réel.
- `appsettings.json` (non tracké ✅ — vérifié par `git ls-files`) contient la `ServiceKey` Supabase et
  le mot de passe DB en clair. Pas de fuite, mais à basculer en variables d'environnement.
