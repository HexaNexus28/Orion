# ORION disponible 24/7 — architecture (2026-08-20)

> Fait suite à une exigence produit énoncée le 2026-08-20 :
> **« je voudrais qu'ORION fonctionne même si le PC n'est pas ouvert »**.
>
> Cette phrase change la nature du projet. Tant qu'ORION vivait sur le PC, « où mettre la base »
> était une question de confort. Elle devient une question de structure.

---

## 1. Ce qui meurt quand le PC s'éteint (état au 2026-08-20)

| Composant | Où il tourne aujourd'hui | PC éteint |
|---|---|---|
| Backend .NET | `localhost:5107` | ❌ |
| Base de données | conteneur Docker local (port 5433) | ❌ |
| LLM | Ollama local (`llama3.2:3b`) | ❌ |
| Embeddings | `nomic-embed-text` local | ❌ |
| Daemon | le PC lui-même | ❌ **et c'est irréductible** |
| Frontend PWA | Vercel | ✅ (mais il ne parle à personne) |

**Aujourd'hui, PC éteint = ORION n'existe pas du tout.** Pas dégradé : absent.

---

## 2. ORION se scinde en deux plans

C'est la clé, et elle n'est pas négociable — elle découle de la physique, pas d'un choix technique.

### Plan « cerveau » — doit tourner en permanence

Backend API · base de données · LLM · embeddings · mémoire long terme · proactivité et briefings ·
PWA. Rien là-dedans n'a de raison d'être lié à ta machine.

### Plan « mains » — lié au PC, par définition

Le daemon et ses 13 outils système : `open_app`, `run_script`, `git_commit`, `write_file`,
`kill_process`, presse-papiers, capture d'écran.

> **Le daemon ne peut pas être déporté.** Ses actions agissent sur *ce* PC. Un daemon sur le VPS
> ouvrirait Notepad sur le VPS — ce qui n'a aucun sens. Ce n'est pas un problème d'architecture,
> c'est de la physique.

Ce qui est architecturable, c'est **le comportement d'ORION quand les mains sont absentes**.

---

## 3. La file d'actions différées — la vraie réponse

Sans elle, PC éteint donne : `"Daemon non connecté"` → échec sec → ORION *paraît* cassé alors qu'il
fonctionne parfaitement. C'est exactement le genre de dégradation muette qui a déjà coûté des mois
sur ce projet (cf. `jarvis-gap-analysis.md` §1.11).

Avec elle :

```
  Toi (téléphone, 22h)  ─→  ORION (VPS, toujours actif)
       « commit le travail sur ShiftStar »
                              │
                              ├─ daemon hors ligne ?
                              │     └─ enfile l'action + répond honnêtement :
                              │        « Ton PC est éteint. Je le fais à son réveil. »
                              │
  PC rallumé  ─→  daemon se reconnecte  ─→  draine la file  ─→  ORION te notifie
```

**Quatre règles, non négociables :**

1. **TTL** — une action non exécutée sous 24 h expire. Un `git_commit` d'hier soir exécuté trois
   jours plus tard est une surprise, pas un service.
2. **Aucune action destructive mise en file sans confirmation explicite** — `run_script`,
   `write_file`, `kill_process` s'exécuteraient sur un état que tu n'as pas vu. Elles se
   redemandent, elles ne se rejouent pas.
3. **File visible dans l'UI** — ce qui attend doit être annulable d'un geste.
4. **ORION dit toujours ce qu'il ne peut pas faire.** Jamais de silence, jamais de faux succès.

---

## 4. Conséquence sur la base de données

Le conteneur Docker local **meurt avec le PC** : il ne peut pas porter l'ORION 24/7.

Mais il **garde un rôle réel** — le développement : hors-ligne, instantané, jetable, aucun risque
sur les vraies données. C'est exactement la séparation dev/prod déjà en place sur ShiftStar.

> **Deux bases, deux usages.** Le conteneur ne se jette pas : il change de rôle.

Pour la base de production, un seul critère décide : **elle doit être joignable par le backend
toujours-actif**. D'où la question ouverte : *où vit le plan cerveau ?*

| | VPS IONOS | Render |
|---|---|---|
| Coût | **déjà payé** (9-15 €/mois, mutualisé) | gratuit = s'endort (incompatible avec la proactivité) ; ~7 $/mois sinon |
| Cohérence | pattern maison : une stack Docker par projet, port loopback `127.0.0.1:80XX` derrière la façade Nginx | plateforme séparée à gérer |
| Base | Supabase self-hosted déjà en place | Supabase Cloud |
| Réserve | ⚠️ **« charge serrée » documentée** (`infrastructure/CLAUDE.md:602-628`) | aucune |
| Ops | Ansible, backups R2, à ta main | rien à faire |

### Sur la pause Supabase Cloud

Le projet s'est mis en pause **parce qu'ORION était mort**. Un ORION réellement actif 24/7
n'est jamais inactif — **le problème de pause se résout tout seul** dès que l'exigence de ce
document est satisfaite. Ce n'était pas une raison de fuir Supabase Cloud, c'était un symptôme du
point mort. Correction de ce que j'ai affirmé plus tôt : le projet n'était **pas supprimé**, mais
en pause — l'observation `NXDOMAIN` était juste, la déduction ne l'était pas.

---

## 5. Le point dur à trancher AVANT d'accumuler de la mémoire

Les embeddings locaux (`nomic-embed-text`) tombent aussi quand le PC s'éteint. Deux sorties :

- **NIM** — à vérifier : le catalogue contient-il un modèle d'embedding exploitable ?
- **`nomic-embed-text` sur le VPS** — 274 Mo, tourne en CPU sans GPU. Modeste, mais c'est une
  charge de plus sur un VPS déjà serré.

> ⚠️ **`memory_vectors.embedding` est déclaré `vector(768)` dans `memory/schema.sql`.**
> Changer de modèle d'embedding change la dimension → migration de schéma **et revectorisation de
> toute la mémoire accumulée**. Tant que la table est vide, ce choix est gratuit. Chaque jour de
> mémoire accumulée le rend plus cher.
>
> **Donc : trancher les embeddings AVANT le chantier 4 (mémoire), pas après.**
>
> 🔴 **Ça n'a pas été fait.** J4 a été livré le 2026-08-20 sur `nomic-embed-text` **local**, et la
> table n'est plus vide. Chaque jour de mémoire accumulée renchérit la bascule. C'est aujourd'hui
> le seul vrai bloquant du déploiement : sur le VPS il n'y a pas d'Ollama, donc la recherche
> mémoire atterrirait morte. **J6b avant J6c, sans exception.**

---

## 6. Où ça s'insère dans le plan

L'ordre des chantiers ne change pas — celui-ci s'ajoute, il ne double pas les autres.

| Chantier | État |
|---|---|
| J1 Boucle agent | ✅ livré et prouvé e2e |
| J2 Prompts | ✅ livré (2026-08-20) |
| J3 Cerveau NIM | ✅ livré (2026-08-20) — **le choix d'embedding distant n'a PAS été tranché** (§5) |
| J4 Mémoire | ✅ livré (2026-08-20), sur embeddings **locaux** — d'où la dette ci-dessous |
| J5 Proactivité | ✅ livré (2026-08-20), 5 étages |
| **J6a File d'actions différées** | ✅ **livré et prouvé en exécution réelle (2026-08-21)** |
| **J6b Embedding distant** | ⛔ **bloquant du déploiement** — voir §5, et la dette a commencé à courir |
| **J6c Déploiement VPS** | à faire — dépend de J6b |

**J6a n'attendait pas le déploiement** : il se teste en local en coupant simplement le daemon,
et c'est lui qui rend le déploiement présentable — sans lui, chaque demande d'action avec le PC
éteint répondrait « Daemon non connecté ».

### Ce que J6a a réellement livré (2026-08-21)

Point d'application **unique** : `IToolInvoker`. Avant lui, deux appelants exécutaient les outils
chacun de leur côté et le garde « daemon absent » était recopié dans **treize** outils. Les treize
copies ont disparu — une règle recopiée n'est pas une règle, c'est treize endroits où l'oublier.

Un troisième drapeau sur `ITool` : **`IsDeferrable`**. Le catalogue se trie désormais par
**utilité différée**, pas par disponibilité.

| | PC éteint | pourquoi |
|---|---|---|
| `open_app`, `open_browser_url`, `git_commit`, `write_file` | **mis en file** | l'effet voulu est le même demain matin |
| `read_file`, `list_files`, `git_status`, `get_system_status`, `capture_screen`, `clipboard`, `type_text`, `kill_process` | **refus franc** | une réponse sur l'état d'hier ne vaut rien |
| `run_script` | **refus franc** | voir ci-dessous |

⚠️ **`run_script` a d'abord été déclaré différable, puis retiré — trouvé à l'essai réel.**
`list_files` étant hors catalogue, le modèle a **substitué** `run_script` avec un `Get-ChildItem`.
La lecture refusée par la porte revenait par la fenêtre, et l'utilisateur se voyait promettre pour
le lendemain une réponse qu'il voulait tout de suite. Un script est arbitraire : impossible de
savoir s'il lit ou s'il écrit, donc impossible de savoir si le différer garde un sens.
**On ne diffère pas ce qu'on ne comprend pas.** Verrouillé par `ToolDeferrabilityTests`.

Les quatre règles du §3 sont tenues, et prouvées :

| Règle | Où elle vit | Preuve |
|---|---|---|
| TTL 24 h | `expires_at` en base, pas dans le code | balayage périodique + test « expire au lieu de s'exécuter » |
| Destructive = redemandée | `DeferredActionService.DrainAsync` | `write_file` et `git_commit` passés en `awaiting_confirmation` au réveil, jamais rejoués |
| File visible et annulable | `DeferredActionsController` + pastille/overlay | annulation d'un `run_script` et d'un `git_commit` en direct |
| Jamais de silence | `ToolInvoker.RefuserFranchement` + repli de réponse vide | ORION a répondu « ton PC est éteint », puis a écrit le fichier au réveil après confirmation |

**Essai du 2026-08-21, sans daemon puis avec** : « Ouvre Notepad » → mis en file → daemon lancé →
**Notepad tourne (PID 27048)**. « Écris ce fichier » → mis en file → au réveil, attente de
confirmation → confirmé → **fichier présent sur le disque avec le bon contenu**. « Liste ce
dossier » → refus honnête, rien en file.
