# ORION → Jarvis — Architecture cible

> Suite de [`jarvis-gap-analysis.md`](jarvis-gap-analysis.md). Le diagnostic dit ce qui est cassé.
> Ce document dit ce qu'on construit, et **où placer l'argent**.

---

## 1. Où est le « centre d'intelligence » — la question mal posée

L'intuition « je m'achète une machine et j'héberge mon intelligence dessus » est juste sur
l'intention, fausse sur la couche. Un Jarvis a **deux moitiés**, et elles n'ont ni la même valeur
ni le même coût.

| | Le **cerveau** (poids du LLM) | Le **système nerveux** (le reste) |
|---|---|---|
| Quoi | Le modèle qui génère du texte | Mémoire, outils, boucle agent, proactivité, voix, identité |
| Remplaçable ? | **Oui, en une ligne de config** | Non — c'est ce qui rend ORION *tien* |
| Se périme ? | En ~6 mois (cf. 4 modèles retirés en juillet 2026) | Non, il s'enrichit avec le temps |
| Coût pour l'héberger | élevé — il faut un GPU | marginal — tourne sur l'hébergement déjà en place |
| Différenciant | **Zéro** — tout le monde a accès aux mêmes modèles | **Tout** — personne d'autre n'a ta mémoire |

> **Règle de décision : on loue le cerveau, on possède le système nerveux.**

Ce qui fait de The Machine « The Machine », ce n'est pas la taille de son modèle — c'est qu'elle
**observe en continu, se souvient, et se réécrit**. Ces trois choses tournent sur un VPS
modeste, sans GPU.

---

## 2. Peut-on héberger un LLM sur le VPS actuel ?

**Non.** Deux raisons, toutes deux dirimantes.

**2.1 — La RAM est déjà engagée.** La machine est **mutualisée** : elle héberge déjà d'autres
services en production, plus une base et du monitoring. La réserve disponible est faible, et c'est
documenté comme tel.

Y ajouter un modèle 8B (5–6 Go résidents) rejouerait un incident déjà vécu : un service qui sature
la RAM → OOM → black-out de tout ce qui tourne à côté.
**On ne met pas une charge élastique et gourmande à côté d'une production.**

**2.2 — Plus de RAM ne rend pas le modèle plus rapide.** C'est le contresens qui coûte cher :

- La **RAM** décide si le modèle *rentre*.
- La **bande passante mémoire + le GPU** décident à quelle *vitesse* il tourne.

Prendre un VPS deux fois plus gros **sans GPU** permet de charger un modèle 14B… qui tournera
**plus lentement** que le 3B actuel (≈2–3 tokens/s au lieu de 8). On paierait plus cher pour un
Jarvis plus lent. Mesuré : `llama3.2:3b` = **8 tokens/s** en CPU. Un VPS CPU ne fera pas mieux.

---

## 3. Faut-il acheter un VPS GPU ?

Un GPU de 16 Go de VRAM — le minimum pour un 8B en FP16 ou un 14B quantisé — se loue, selon les
offres du marché, **à environ un ordre de grandeur au-dessus du coût de l'hébergement actuel**.

Et ce qu'on obtient pour ce prix est un modèle 8B/14B : un raisonnement **nettement en dessous** de
ce qu'une API facture quelques euros par mois à usage personnel. Le rapport qualité/prix est donc
défavorable dans les deux sens.

**Verdict : non, pas maintenant.** Un GPU se justifie quand (a) le volume est tel que le coût à
l'usage dépasse le forfait, (b) la confidentialité interdit l'envoi externe, ou (c) le modèle est
fine-tuné maison. Aucun des trois n'est vrai aujourd'hui.

**Quand ça deviendra juste** : le jour où des données traitées ne pourront plus sortir de
l'infrastructure. Là, le GPU devient une ligne de coût justifiée par une contrainte, pas une
dépense de confort — et il sera loué à la demande, pas possédé.

---

## 3bis. NVIDIA NIM — l'option qui rend la question du GPU caduque

**NIM n'est pas une offre de serveurs bon marché** — c'est le format de conteneur d'inférence de
NVIDIA. Mais NVIDIA expose ce format en **API hébergée** sur `build.nvidia.com`, et c'est là que
c'est intéressant :

| Critère | NVIDIA Build (NIM hébergé) |
|---|---|
| Prix | **Gratuit, sans limite de durée**, sans carte bancaire (≈1 000 crédits offerts à l'inscription ; les modèles « free » ne consomment pas de crédits) |
| Débit | **40 requêtes/minute** (extensible à 200 sur demande) |
| Modèles | 100+ — DeepSeek V4 Pro, Llama, Qwen, Mistral, Nemotron |
| API | **OpenAI-compatible** (`POST /v1/chat/completions`) |
| Tool calling | ✅ supporté |
| Streaming | ✅ supporté |
| Tarif au-delà du gratuit | 0,10 $ à 10 $ / M tokens selon le modèle |

Pour **un seul utilisateur**, 40 req/min est très largement au-dessus du besoin. On obtient donc un
raisonnement de classe frontier, avec outils et streaming, **sans acheter le moindre GPU**.

### Ce que ça impose à l'architecture

L'API est OpenAI-compatible — **et Ollama expose aussi `/v1/chat/completions`**. Conséquence directe,
et elle simplifie tout :

> **Un seul `OpenAiCompatibleLLMClient`, N URLs de base.**
> NIM, Ollama local, Groq, OpenRouter, Together, un vLLM auto-hébergé plus tard : même code,
> une ligne de config. Le client Ollama maison de 350 lignes disparaît.

### Le piège à ne pas rejouer

Un palier gratuit sans SLA, dont les modèles peuvent être retirés — c'est **exactement** ce qui vient
de se produire avec Ollama Cloud (§1.10 du diagnostic : 4 modèles retirés, 3 verrouillés, en silence).

La parade est architecturale, pas contractuelle :

1. **Sonde au démarrage** — ORION *appelle* réellement chaque modèle configuré et refuse de démarrer
   en silence si le primaire est mort. Jamais deux fois le même aveuglement.
2. **Cascade explicite et loguée** — NIM (qualité, palier gratuit) → API payante en option →
   `llama3.2:3b` local (hors-ligne, dégradé). Chaque bascule est visible dans l'UI, pas masquée.
3. **Le provider est une ligne de config**, jamais une dépendance en dur.

**Recommandation : NVIDIA NIM en primaire, local en filet hors-ligne, une API payante en escalade
optionnelle.** Zéro achat, zéro abonnement, et la décision GPU est repoussée au moment où elle aura
une justification produit.

---

## 4. Ce qu'on héberge réellement — le vrai centre d'intelligence

Tout sauf les poids du modèle. Et c'est là qu'est la valeur.

```
   VPS mutualisé, CPU sans GPU                    Cerveau loué (interchangeable)
  ┌──────────────────────────────────────┐        ┌──────────────────────────┐
  │  Mémoire      pgvector + episodes    │        │  API distante             │
  │               + règles auto-écrites  │◄──────►│  (Claude / Ollama Cloud)  │
  │  Boucle       AgentLoop + outils     │        └──────────────────────────┘
  │  Événements   watchers → scoring     │                    ▲
  │  Embeddings   nomic-embed-text  ✅   │────────────────────┘
  │               274 Mo, CPU            │   les embeddings restent en local
  └──────────────────────────────────────┘
```

`nomic-embed-text` (274 Mo) tourne en CPU sans gêner personne : c'est **la mémoire qui reste
locale**. Le substrat de l'intelligence est auto-hébergé ; seule la génération de texte est louée.

---

## 5. Une intelligence qui écrit ses propres règles

C'est la demande centrale : *« une intelligence qui réfléchit, avec qui on converse, et qui écrit
ses propres règles au fur et à mesure »*. Voici l'architecture, en 4 étages.

### Étage 1 — Mémoire épisodique (ce qui s'est passé)

Chaque tour de conversation est stocké **et vectorisé automatiquement** — pas seulement quand on le
demande. C'est le correctif du §1.5 du diagnostic : aujourd'hui la table reste vide, donc ORION
repart de zéro à chaque session. Un Jarvis amnésique n'est pas un Jarvis.

### Étage 2 — Consolidation (ce qu'il faut en retenir)

Une passe périodique — la « réflexion » — relit les épisodes récents et **distille des faits
durables**. C'est là qu'ORION écrit ses propres règles. Déclenchement : fin de session, ou toutes
les N interactions, ou sur inactivité.

> Le tool `memory_reflect` existe déjà (`Tools/Memory/MemoryReflectTool.cs`) — il n'est simplement
> jamais appelable, faute de boucle. L'ossature est là.

### Étage 3 — Le schéma fermé : la clé de voûte

C'est **le** point que la plupart des projets ratent. Une IA qui écrit librement sa mémoire produit
au bout d'un mois 200 fichiers contradictoires que plus personne ne lit. La mémoire devient du bruit.

La parade est connue et déjà éprouvée ailleurs : **schéma fermé à 4 slots, interdiction absolue
de créer un cinquième fichier.**

| Slot | Contenu | Mode |
|---|---|---|
| `rules` | Comment ORION doit se comporter (corrections, posture) | APPEND |
| `decisions` | Décisions durables + le *pourquoi*, datées | APPEND |
| `state` | Ce qui est en cours — se périme | OVERWRITE |
| `refs` | Pointeurs : chemins, ports, identifiants | APPEND |

Test d'affectation : *« est-ce encore vrai dans 6 mois ? »* → oui : `rules`/`decisions`/`refs` ;
non : `state`.

> **Le problème a déjà été résolu ailleurs ; on applique la même solution à ORION.**
> Ce n'est pas une analogie : c'est littéralement le même problème — une IA qui accumule du
> contexte à travers des sessions sans mémoire partagée.

### Étage 4 — Le garde-fou d'écriture

Une IA qui se réécrit sans contrôle dérive. Quatre verrous, non négociables :

1. **Provenance** — chaque règle cite les épisodes qui l'ont produite. Une règle sans source est
   supprimable sans discussion.
2. **Seuil de promotion** — une observation devient une règle après avoir été confirmée *N* fois,
   pas au premier passage. Ça filtre l'anecdote.
3. **Portée d'écriture** — ORION écrit librement dans `state`, propose pour `rules`/`decisions`.
   La promotion en règle durable passe par toi. *L'autonomie se gagne, elle ne se décrète pas.*
4. **Révocation** — une règle contredite par les faits est retirée, pas empilée à côté de sa
   contradiction : consolider en place, jamais empiler des correctifs.

### Étage 5 — Ce qui fait « The Machine » plutôt qu'un chatbot qui se souvient

La proactivité. Les watchers du daemon existent déjà et produisent des événements. Il manque la
boucle de décision : **événement → scoring d'urgence → ORION décide de parler ou de se taire**.

C'est la différence de nature : un assistant répond quand on l'appelle ; une entité observe et
choisit d'intervenir. Sans cet étage, tout le reste reste un très bon chatbot.

---

## 6. Ordre de construction

Chaque étape est inutile sans la précédente. Cet ordre n'est pas négociable.

| # | Chantier | Débloque | Achat requis |
|---|---|---|---|
| **1** | `AgentLoop` — boucle multi-tours, outils branchés en streaming, événements typés vers l'UI | **Tout le reste.** Prouvé faisable en local (voir §1bis du diagnostic) | aucun |
| **2** | Prompts — prompt agent, vraie liste d'outils injectée, prompt voix, garde-fous destructifs | La qualité des décisions d'ORION | aucun |
| **3** | Cerveau — un `OpenAiCompatibleLLMClient` unique, cascade NIM → Claude → local, sonde au démarrage | Le raisonnement multi-étapes | **aucun** (§3bis) |
| **4** | Mémoire — écriture auto, consolidation, schéma fermé 4 slots, garde-fous | La continuité entre sessions | aucun |
| **5** | Proactivité — watchers → scoring → prise de parole | Le passage assistant → entité | aucun |

**Le chantier 1 ne dépend d'aucune décision d'achat** : il se construit et se teste sur
`llama3.2:3b` en local, sans rien dépenser. C'est pour ça qu'il passe en premier — il transforme
la décision « cerveau » en réglage, au lieu d'un préalable bloquant.
