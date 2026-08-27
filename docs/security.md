# Sécurité — ORION

> Audit du 2026-08-27, sur l'état réel du dépôt (branche `claude/docs-agents-security-audit-cga1pl`).
> Ce document n'est pas une checklist théorique : chaque constat a été vérifié dans le code, et
> chaque ligne « Vérifié » nomme le fichier qui le prouve.

## 1. Le modèle de menace, en une phrase

ORION est **authentifié comme son propriétaire** et **exécute des actions sur sa machine**. Les
deux à la fois. Donc le contrôle d'accès ne protège que la moitié du problème : une fois la
session ouverte, tout ce que le modèle décide de faire part avec les droits complets de
l'utilisateur. La question n'est pas « qui appelle ? » — elle est réglée — mais **« ce que le
modèle vient de décider, doit-il vraiment partir ? »**

Cette distinction n'est pas rhétorique. `ToolInvoker` la formule déjà dans son propre commentaire :

> ORION lit le web (`web_browse`, `web_fetch`). Une page peut contenir des instructions qui
> détournent le modèle, et la requête résultante est parfaitement AUTHENTIFIÉE — aucun contrôle
> d'accès ne peut l'arrêter.

Tout ce qui suit découle de là. **L'injection de prompt est le vecteur principal**, et le
périmètre des outils est la seule défense qui la couvre.

## 2. Ce qui est solide

Le bord de l'API a été durci et tient. À conserver tel quel.

| Contrôle | Vérifié dans |
|---|---|
| Fermé par défaut — `FallbackPolicy` exige le propriétaire, une route oubliée est refusée | `Program.cs` |
| Fail-closed sur secret absent — jeton daemon vide = REFUS, pas ouverture | `DaemonAuthenticationHandler.cs` |
| Comparaison à temps constant sur les deux secrets (mot de passe, jeton daemon) | `AuthController.cs`, `DaemonAuthenticationHandler.cs` |
| Billet de flux à audience distincte, 60 s, seul autorisé dans une URL — et refusé hors des chemins de flux | `AuthController.cs`, `Program.cs` (`OnTokenValidated`) |
| Refus de démarrer si aucune origine WebSocket n'est déclarée (liste vide = tout accepté) | `Program.cs` |
| Journalisation du chemin complet coupée en production (le billet fuyait en clair) | `Program.cs` |
| Une seule fabrique de jetons — session et billet ne peuvent pas diverger | `AuthController.cs` |
| Secrets hors dépôt (`appsettings.Development.json`, `.env`, `*.key`, `*.pfx` ignorés) | `.gitignore` |
| Confirmation obligatoire des actions destructives, PC allumé compris | `ToolInvoker.cs` |

Le point le plus fort est le dernier : le garde-fou a été déplacé **du prompt système vers le
code**. Une phrase dans un prompt est une suggestion ; `ToolInvoker.IsDestructive` est une règle.

## 3. Le fil rouge de l'audit : des garde-fous déclarés, jamais appliqués

Les quatre constats qui suivent partagent une seule cause. Le projet a **prévu** les limites — il
existe une classe de configuration pour les porter — mais **rien ne les lit**. Un lecteur qui
ouvre `DaemonOptions` ou `InternetOptions` croit voir une défense. Il n'y en a pas.

C'est le pire état possible : moins sûr qu'une absence de garde-fou, parce qu'il ne se voit pas.

```
Injection dans une page web
  → le modèle décide d'appeler read_file
  → ToolInvoker : IsDestructive == false  → EXÉCUTION IMMÉDIATE, sans confirmation
  → ReadFileTool : passe le chemin tel quel
  → ReadFileAction : Path.GetFullPath(path)  → AUCUNE restriction
  → contenu renvoyé au modèle → recraché dans sa réponse → exfiltré
```

Aucun maillon de cette chaîne ne dit non.

---

### C1 — CRITIQUE — Lecture du disque sans périmètre, et sans confirmation

**Où** : `daemon/Orion.Daemon.Actions/ReadFileAction.cs`, `ListFilesAction.cs`
**Outils** : `read_file`, `list_files`

`ReadFileAction` reçoit `DaemonOptions` par injection **et ne s'en sert jamais** (`grep '_options\.'`
sur `Orion.Daemon.Actions/` : zéro résultat). Le chemin est simplement normalisé :

```csharp
var fullPath = Path.GetFullPath(path);   // et c'est tout
```

Aucune racine autorisée, aucun refus. `list_files` accepte en plus `recursive: true` : un seul
appel sur `C:\Users\<moi>` énumère tout le profil.

**Ce qui aggrave** : les deux outils ont `IsDestructive == false`. Ils ne passent donc **pas** par
la file de confirmation — ils partent immédiatement. Le seul garde-fou du projet ne les couvre pas.

**Impact concret** : `%USERPROFILE%\.ssh\id_rsa`, `.env` de n'importe quel projet,
`appsettings.Production.json` du daemon (qui contient `DAEMON_WS_TOKEN` — soit la compromission du
canal lui-même), bases de cookies des navigateurs. Le contenu revient au modèle, qui le restitue
dans sa réponse : **la lecture est l'exfiltration**, aucun canal sortant supplémentaire n'est requis.

**Correctif** : une racine autorisée (`AllowedRoots`) portée par `DaemonOptions`, appliquée après
`GetFullPath` et **avant** tout accès disque. Comparaison sur le chemin normalisé, pas sur la
chaîne d'entrée — sinon `..\..\` la contourne. Refus par défaut si la liste est vide.

---

### C2 — CRITIQUE — Écriture du disque sans périmètre

**Où** : `daemon/Orion.Daemon.Actions/WriteFileAction.cs` · **Outil** : `write_file`

Même dette, côté écriture, avec `Directory.CreateDirectory` en prime :

```csharp
var fullPath = Path.GetFullPath(path);
Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
File.WriteAllText(fullPath, content ?? "");
```

Écrit **n'importe où** où l'utilisateur a le droit d'écrire, en créant l'arborescence au passage.
Cible évidente : le dossier Démarrage — or c'est précisément par là que le daemon lui-même est
lancé (cf. [daemon.md](daemon.md)). Un fichier déposé là s'exécute à la prochaine ouverture de
session. **Persistance complète.**

**Atténuation réelle** : `IsDestructive == true`, donc confirmation exigée. C'est ce qui le
maintient en C2 et non au-dessus. Mais la confirmation affiche un chemin ; elle ne le *valide*
pas, et un utilisateur qui confirme vite ne relit pas une chaîne longue.

**Correctif** : même `AllowedRoots` que C1. La confirmation reste, elle ne remplace pas le périmètre.

---

### E1 — ÉLEVÉ — `run_script` : le guillemet casse la commande

**Où** : `daemon/Orion.Daemon.Actions/RunScriptAction.cs` · **Outil** : `run_script`

```csharp
Arguments = $"-ExecutionPolicy Bypass -Command \"{script}\"",
```

`run_script` exécute du code arbitraire **par conception** — ce n'est pas le constat. Le constat
est que **l'échappement est absent** : un `script` contenant un guillemet double termine
l'argument, et ce qui suit est interprété par `powershell.exe` comme des options à lui. La
commande réellement lancée cesse de correspondre à celle qui a été confirmée par l'utilisateur.

Une confirmation qui porte sur un texte différent de ce qui s'exécute n'est pas une confirmation.

**Correctif** : passer le script en `-EncodedCommand` (base64 UTF-16LE). Il n'y a alors plus
aucune frontière de guillemet à casser, et ce qui s'exécute est exactement ce qui a été affiché.

---

### E2 — ÉLEVÉ — SSRF : `web_fetch` accepte n'importe quelle URI

**Où** : `backend/Orion.Business/Tools/Internet/WebFetchTool.cs`, `WebBrowseTool.cs`

```csharp
if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))   // seul contrôle
```

Absolue, et c'est tout. Ni schéma, ni hôte.

`InternetOptions.BlockedDomains` existe. **`grep -rn "BlockedDomains" backend/ --include=*.cs`
renvoie une seule ligne : sa propre déclaration.** Personne ne la lit. `WebFetchTool` n'injecte
même pas `InternetOptions`.

**Atteignable** : `http://127.0.0.1:5107/api/...` (l'API elle-même, en loopback, où le filtrage
d'origine ne s'applique pas), `http://169.254.169.254/` (métadonnées d'instance sur un VPS
cloud), `file:///` selon le handler. Et `web_fetch` n'étant pas destructif, il part sans
confirmation — c'est aussi la porte d'entrée de l'injection de prompt décrite au §1.

**Correctif** : n'autoriser que `http`/`https`, résoudre l'hôte et **refuser les adresses privées,
loopback et link-local** (résolution incluse, sinon un nom DNS pointant sur 127.0.0.1 passe), puis
appliquer `BlockedDomains` — ou supprimer le champ s'il ne doit pas servir. Une option morte dans
une classe de configuration est un mensonge pour le prochain lecteur.

---

### M1 — MOYEN — `kill_process` par nom tue toute la famille

**Où** : `daemon/Orion.Daemon.Actions/KillProcessAction.cs`

`Process.GetProcessesByName(name)` puis `Kill(entireProcessTree: true)` **sur chaque résultat**.
`kill_process("chrome")` ferme toutes les fenêtres du navigateur, pas une. L'outil est destructif
donc confirmé, mais la confirmation annonce un nom et l'effet porte sur N processus.
**Correctif** : annoncer le compte et les PID dans la demande de confirmation.

### M2 — MOYEN — `ReadFileAction` lit le fichier trois fois

`File.ReadLines(fullPath)` est appelé **trois fois** (contenu, `totalLines`, `truncated`) : trois
parcours disque, et trois instants différents — un fichier qui change entre les deux donne un
`truncated` incohérent. **Correctif** : matérialiser une fois en liste, calculer sur elle.

### M3 — MOYEN — `-ExecutionPolicy Bypass` dans la doc d'installation

`docs/daemon.md` documente l'installation via `powershell -ExecutionPolicy Bypass -File ...`. C'est
courant et ici assumé (script local, non élevé), mais cela entraîne l'utilisateur à contourner la
politique par réflexe. **Correctif** : documenter `Unblock-File` comme alternative propre.

---

## 4. Tableau de bord

| # | Sévérité | Constat | Confirmation ? | Périmètre ? |
|---|---|---|---|---|
| C1 | 🔴 Critique | `read_file` / `list_files` — tout le disque | ❌ non | ❌ aucun |
| C2 | 🔴 Critique | `write_file` — tout le disque | ✅ oui | ❌ aucun |
| E1 | 🟠 Élevé | `run_script` — échappement absent | ✅ oui | n/a |
| E2 | 🟠 Élevé | `web_fetch` / `web_browse` — SSRF, `BlockedDomains` mort | ❌ non | ❌ aucun |
| M1 | 🟡 Moyen | `kill_process` — tue N processus pour un nom | ✅ oui | n/a |
| M2 | 🟡 Moyen | `ReadFileAction` — triple lecture disque | — | — |
| M3 | 🟡 Moyen | `Bypass` enseigné par la doc | — | — |

**Ordre de traitement** : C1 d'abord — c'est le seul qui soit à la fois sans périmètre **et** sans
confirmation. C2 partage son correctif : les deux se referment avec le même `AllowedRoots`.

## 5. La règle qui manquait

À ajouter aux invariants, parce que c'est la leçon commune des quatre constats :

> **Un outil qui touche au disque, au réseau interne ou aux processus doit porter un PÉRIMÈTRE
> explicite, appliqué dans le code qui agit — pas dans le prompt, pas dans une classe de
> configuration que personne ne lit.** Une option déclarée et jamais lue est pire que son absence :
> elle se fait passer pour une défense.

Et son corollaire, déjà vrai pour `IsDeferrable` :

> **Le défaut est le refus.** Périmètre vide = rien n'est autorisé, pas « tout est autorisé ».
> `DaemonAuthenticationHandler` applique déjà ce principe au jeton ; le disque le mérite autant.

## 6. Hors périmètre de cet audit

Non examinés — à traiter séparément, sans les considérer comme sains :

- **Injection SQL / RLS Supabase** : le Repository Pattern et EF Core paramètrent les requêtes,
  mais les politiques RLS côté Supabase n'ont pas été relues.
- **Chaîne d'approvisionnement** : versions NuGet et npm non auditées (`dotnet list package
  --vulnerable`, `npm audit`).
- **Rotation des secrets** : aucune procédure documentée pour `DAEMON_WS_TOKEN` / `Auth:JwtSecret`.
  Un jeton de session vit 30 jours et **rien ne permet de le révoquer** avant terme.
- **Limitation de débit** : `/api/auth/login` n'a aucun frein. Le mot de passe unique est donc
  attaquable en force brute — la comparaison à temps constant ne protège pas de ça.
