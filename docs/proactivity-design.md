# ORION — Proactivité : ce qui existe, ce qui manque (2026-08-20)

> Analyse du code réel, pas de la doc. Chaque affirmation est adossée à un `fichier:ligne`.

## 1. Ce qui existe et fonctionne

Cinq watchers, huit patterns, une chaîne complète jusqu'à la voix — et elle marche : les
notifications RAM arrivent réellement.

| Watcher | Patterns émis |
|---|---|
| `ActivityWatcher` | `skip_meal`, `overwork` |
| `TimeWatcher` | `meal_time`, `break_time`, `night_time` |
| `SystemWatcher` | `high_cpu`, `high_ram` |
| `ProcessWatcher` | surveille les applications au premier plan |
| `AdaptiveWatcher` | `adaptive_morning_routine` |

Le flux : watcher → `POST /api/proactivenotification/trigger` → le LLM rédige le message →
diffusion SSE au front **et** TTS local si le navigateur est fermé.

---

## 2. Le défaut central : il n'y a aucune décision

`ProactiveOrchestrator.OnPatternDetected` (`:80-104`) fait exactement ceci :

```
pattern détecté  →  GenerateProactiveMessage()  →  NotifyAll()
```

**Tout pattern détecté devient une parole. Immédiatement. Sans condition.**

Il n'y a ni score, ni filtre, ni arbitrage. Un `high_ram` à 96 % et un `meal_time` à 13 h ont
exactement le même poids. ORION ne décide pas de parler — il n'a simplement aucun moyen de
se taire.

### L'anti-spam est au mauvais endroit

`SystemWatcher.cs:23` porte un `COOLDOWN` de 15 minutes et un dictionnaire `_lastTriggered`.
**Les quatre autres watchers n'en ont pas.** La protection anti-répétition vit donc dans *un*
watcher au lieu d'un point d'application unique — chaque nouveau watcher devra la réécrire,
ou l'oubliera.

C'est le même défaut structurel que celui corrigé avec `AgentLoop` : une règle qui doit valoir
pour tous ne peut pas vivre dans l'un des participants.

---

## 3. Le vrai angle mort : le SUJET, pas la technique

Les huit patterns actuels sont de l'hygiène de vie : mange, dors, fais une pause, ton CPU
chauffe, ta RAM est pleine.

> **Aucun ne concerne son travail.**

Un Jarvis ne rappelle pas de manger — il surveille ce qui compte pour toi. ORION dispose déjà
de 22 outils (`git_status`, `web_fetch`, `get_system_status`, `run_script`…) : la matière
première existe, personne ne l'a branchée sur la proactivité.

Ce qu'un watcher « métier » dirait :

- « Ton build ShiftStar a échoué il y a dix minutes. »
- « Le VPS ne répond plus depuis six minutes. »
- « Tu as un commit non poussé depuis trois jours sur ORION. »
- « Il te reste 12 % de ton quota NIM. »

### L'exemple qui prouve l'argument

**Le projet Supabase d'ORION s'est mis en pause faute d'activité.** Résultat : plus de base,
un « RAG indisponible » silencieux, et des heures perdues à diagnostiquer un projet réputé
mort. Un watcher unique — *« le projet Supabase n'a pas reçu de requête depuis N jours »* —
aurait transformé une panne en une phrase dite trois jours avant.

C'est ça, la proactivité utile : pas un rappel de repas, un incident évité.

---

## 4. Parler, ou AGIR

Aujourd'hui, proactif = notifier. Or ORION sait maintenant enchaîner des outils.

| Aujourd'hui | Possible |
|---|---|
| « Ta RAM est à 96 %. » | « Ta RAM était à 96 %, j'ai fermé 12 onglets inactifs — tu es à 71 %. » |
| « Ton build a échoué. » | « Ton build a échoué : test `X` cassé, ligne 42. Je te montre le diff ? » |

Les garde-fous nécessaires **sont déjà dans le prompt** : les outils destructifs exigent une
demande explicite, et un outil qui ne peut pas aboutir est retiré du catalogue. La différence
entre un réveil et un assistant tient à ça.

---

## 5. La boucle d'apprentissage : déclarée, jamais construite

La table `behavior_patterns` existe. Son entité aussi, son repository avec trois méthodes de
requête, son `DbSet`, son mapping EF.

```
grep "BehaviorPatterns\." backend daemon  →  aucun résultat
```

**Zéro écriture, zéro lecture.** Sa colonne `orion_response` était prévue pour enregistrer
comment ORION a réagi — le commentaire du schéma dit littéralement « pour apprendre ».

C'est pourtant elle qui permettrait la seule chose qui rende la proactivité supportable dans la
durée : **arrêter de dire ce que l'utilisateur ignore systématiquement.** Un pattern signalé
dix fois et jamais suivi d'effet doit se taire tout seul.

---

## 6. Quatre leviers, par rapport valeur/coût

### 6.1 Un budget d'attention (le plus rentable)

Un nombre fini d'interruptions par heure. Un événement à score élevé peut « dépenser » plus.
C'est ce qui sépare un collègue d'un spammeur de notifications — et ça se code en une classe,
dans l'orchestrateur, une fois pour tous les watchers.

### 6.2 Interrompre ou différer

`BriefingScheduler` tourne déjà tous les jours à 08:00. Tout ce qui n'est pas urgent n'a aucune
raison d'interrompre : ça s'accumule et se dit au briefing du lendemain.

```
score élevé      → parler maintenant
score moyen      → attendre une pause d'activité (ProcessWatcher sait quand tu changes d'app)
score faible     → verser au briefing de demain
```

Architecture gratuite : les deux pièces existent.

### 6.3 Le contexte, entrée manquante du score

ORION voit l'heure et les métriques système. Il ne voit pas **ce que tu fais**.
`ProcessWatcher` connaît pourtant l'application au premier plan.

De quoi distinguer deux situations que tout oppose aujourd'hui :
- VS Code au premier plan depuis 40 min → travail concentré → **ne pas** interrompre pour `meal_time`,
  mais **interrompre** pour `high_ram`, qui menace précisément ce travail ;
- navigateur, activité dispersée → n'importe quel rappel passe sans coût.

### 6.4 Des watchers qui regardent le travail

Le plus gros gain, et le moins cher : les watchers actuels observent la *machine*. Il en
manque qui observent le *monde de l'utilisateur* — dépôts git, CI, VPS, quotas, échéances.
Un `WorkWatcher` interrogeant périodiquement les outils déjà en place suffirait.

---

## 7. Ce que ça change dans la nature du produit

| | Aujourd'hui | Avec la boucle de décision |
|---|---|---|
| Déclencheur | un seuil est franchi | un seuil est franchi **et** ça vaut ton attention |
| Contenu | hygiène de vie | ton travail, tes incidents |
| Réaction | il te prévient | il a déjà agi, il te le dit |
| Durée de vie | tu finis par le couper | il se tait tout seul sur ce que tu ignores |

Le passage d'« assistant qui répond » à « entité qui observe » ne tient pas au nombre de
watchers. Il tient à la capacité de **se taire** — et c'est exactement ce qui manque.
