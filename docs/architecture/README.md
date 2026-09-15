# ORION - Documentation Architecture

Cette documentation est organisée en 4 fichiers distincts :

##  Structure

| Fichier | Contenu |
|---------|---------|
| [01-data-architecture.md](./01-data-architecture.md) | **Diagramme ERD**, schéma de base de données, description des tables |
| [02-class-architecture.md](./02-class-architecture.md) | **Diagrammes de classes**, architecture en couches, interfaces |
| [03-flow-architecture.md](./03-flow-architecture.md) | **Flux**, séquences, patterns, règles de code |
| [04-audit.md](./04-audit.md) | **Audit et traçabilité**, IAuditService, table audit_logs |

---

## Pour quelle lecture ?

### Tu veux comprendre la base de données ?
→ Va dans `01-data-architecture.md`

### Tu veux comprendre les classes et leurs relations ?
→ Va dans `02-class-architecture.md`

### Tu veux comprendre le flux d'une requête ou les patterns ?
→ Va dans `03-flow-architecture.md`

---

##  Table des matières

### 01-data-architecture.md
- Diagramme Entité-Relation (ERD)
- Description des tables (conversations, messages, memory_vectors...)
- Schéma SQL

### 02-class-architecture.md
- Diagramme de classes complet
- Règles d'implémentation par couche
- DTOs Internes vs Publics
- Dépendances entre projets

### 03-flow-architecture.md
- Diagramme de séquence (requête chat)
- Architecture hexagonale
- Pattern de retour par couche
- ToolResult vs ApiResponse
- Structure des fichiers
- Règles de code et anti-patterns

---

##  Mise à jour

⚠️ **Ces fichiers ne sont PAS régénérés automatiquement.** Ils ont dérivé une fois : les diagrammes
décrivaient `ILLMClient`, `LLMRouter`, `AnthropicClient`, `MemoryAgent` et `ToolAgent` — cinq classes
dont trois n'ont jamais existé et deux ont été supprimées — tout en affirmant être à jour.

Une documentation qui se déclare à jour sans l'être coûte plus cher que son absence : on cherche un
fichier qui n'existe pas et on conclut qu'on a mal lu. **Toute modification des couches ou du chemin
LLM doit toucher ces diagrammes dans le même commit.**

Vue d'ensemble courte et tenue à jour : [`../architecture.md`](../architecture.md). Ces quatre
fichiers en sont le détail (ERD, diagrammes de classes, séquences, audit).
