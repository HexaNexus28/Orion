# ORION — Système Mémoire

## Vue d'ensemble

ORION a deux niveaux de mémoire distincts qui fonctionnent ensemble.

```
┌─────────────────────────────────────────────────────────┐
│  MÉMOIRE COURT TERME (RAM)                              │
│  Durée : session en cours uniquement                    │
│  Contenu : 20 derniers messages de la conversation      │
│  Effacée : à chaque nouvelle session                    │
└─────────────────────────────────────────────────────────┘
                          +
┌─────────────────────────────────────────────────────────┐
│  MÉMOIRE LONG TERME (Supabase + pgvector)               │
│  Durée : permanente                                     │
│  Contenu : profil Yawo, résumés, faits importants       │
│  Récupérée : via RAG (similarité sémantique)            │
└─────────────────────────────────────────────────────────┘
```

## Tables Supabase

| Table | Rôle | Taille estimée |
|---|---|---|
| `conversations` | Sessions de chat | ~1 KB / session |
| `messages` | Messages individuels | ~3 KB / message |
| `memory_vectors` | Embeddings RAG | ~6 KB / vecteur |
| `user_profile` | Profil clé-valeur Yawo | ~5 KB total |
| `tool_executions` | Log des tools appelés | ~2 KB / appel |

**Estimation 1 an d'usage intensif (10 échanges/jour) :**
- Messages : 3 650 × 3 KB = ~11 MB
- Vecteurs : 3 650 × 6 KB = ~22 MB
- **Total : ~35 MB** — Supabase free tier (500 MB) suffit largement.

## Flux RAG — Étape par Étape

```
1. USER envoie : "Combien d'utilisateurs actifs sur ShiftStar ?"

2. EMBEDDING : Ollama génère un vecteur 768 dimensions pour ce message
   [0.023, -0.412, 0.887, ...]

3. VECTOR SEARCH : pgvector cherche les 5 souvenirs les plus proches
   → "ShiftStar a 40 utilisateurs actifs (février 2026)"
   → "Le pricing ShiftStar est €49/mois Pro"
   → "Yawo travaille chez McDonald's Areas France"

4. CONTEXT BUILD :
   [PROFIL YAWO]
   Nom: Yawo Zoglo, Fondateur ShiftStar...

   [SOUVENIRS PERTINENTS]
   - ShiftStar a 40 utilisateurs actifs...
   - Le pricing ShiftStar est €49/mois...

   [CONVERSATION EN COURS]
   User: Combien d'utilisateurs actifs sur ShiftStar ?

5. LLM RÉPOND avec ce contexte enrichi
   → Appelle tool get_shiftstar_stats pour données fraîches

6. SAVE : Le message + son embedding sont sauvegardés
```

## Profil Utilisateur (user_profile)

Le profil est un store clé-valeur simple, chargé entièrement à chaque session.
Il est injecté en tête de chaque prompt système.

**Clés standards :**
```
name              Yawo Zoglo
role              Fondateur, étudiant, développeur
projects          Liste des projets actifs
priority_now      Focus actuel (VivaTech, Areas France, alternance)
briefing_time     07:00
timezone          Europe/Paris
language          Français
llm_preference    Ollama local (Kimi K2)
```

**Modifier le profil :**
```
User → ORION : "Souviens-toi que mon rendez-vous VivaTech est le 17 juin"
ORION → UPDATE user_profile SET value = '17 juin 2026' WHERE key = 'vivatech_date'
```

## Importance des Souvenirs

Chaque vecteur a un score `importance` (0.0 à 1.0).
Les faits critiques ont une importance haute et sont récupérés en priorité.

```
1.0  → Profil (déjà chargé séparément)
0.9  → Décisions importantes, dates clés
0.7  → Faits sur les projets
0.5  → Conversations générales
0.3  → Anecdotes, contexte léger
```

## Commandes Mémoire (via ORION)

```
"Qu'est-ce que tu sais de moi ?"
→ ORION affiche le profil complet

"Oublie ce que je t'ai dit sur X"
→ DELETE FROM memory_vectors WHERE content LIKE '%X%'

"Souviens-toi que..."
→ INSERT INTO memory_vectors (content, importance) VALUES (...)

"Montre-moi mes derniers souvenirs"
→ SELECT content, created_at FROM memory_vectors ORDER BY created_at DESC LIMIT 20
```

## Embedding Model

```
Modèle    : nomic-embed-text (via Ollama)
Dimension : 768
Commande  : ollama pull nomic-embed-text

Alternatives si nomic indispo :
  - mxbai-embed-large (1024 dims — changer vector(768) en vector(1024))
  - all-minilm (384 dims — moins précis mais plus rapide)
```

## Maintenance

**Nettoyage mensuel recommandé :**
```sql
-- Supprimer souvenirs vieux de +6 mois avec faible importance
DELETE FROM memory_vectors
WHERE importance < 0.4
AND created_at < NOW() - INTERVAL '6 months';

-- Résumé des conversations longues (à faire manuellement via ORION)
-- "ORION, résume et archive notre conversation d'aujourd'hui"
```
