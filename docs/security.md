# Sécurité — ORION

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

### C1 — ✅ CORRIGÉ — Lecture du disque sans périmètre

**Où** : `daemon/Orion.Daemon.Actions/ReadFileAction.cs`, `ListFilesAction.cs`
**Outils** : `read_file`, `list_files`

#### Le constat

`ReadFileAction` recevait `DaemonOptions` par injection **et ne s'en servait jamais**
(`grep '_options\.'` sur `Orion.Daemon.Actions/` : zéro résultat). Le chemin était simplement
normalisé :

```csharp
var fullPath = Path.GetFullPath(path);   // et c'est tout
```

Aucune racine autorisée, aucun refus. `list_files` acceptait en plus `recursive: true` : un seul
appel sur `C:\Users\<moi>` énumérait tout le profil.

**Ce qui aggravait** : les deux outils ont `IsDestructive == false`. Ils ne passaient donc **pas**
par la file de confirmation — ils partaient immédiatement. Le seul garde-fou du projet ne les
couvrait pas.

**Impact** : `%USERPROFILE%\.ssh\id_rsa`, `.env` de n'importe quel projet,
`appsettings.Production.json` du daemon (qui contient `DAEMON_WS_TOKEN` — soit la compromission du
canal lui-même), bases de cookies des navigateurs. Le contenu revenait au modèle, qui le restitue
dans sa réponse : **la lecture est l'exfiltration**, aucun canal sortant supplémentaire n'est requis.

#### Le correctif

`Orion.Daemon.Core/Security/PathScope.cs` — un périmètre partagé par les deux actions, placé
**avant tout accès disque**. `DaemonOptions.AllowedRoots` est désormais **lu**.

| Règle | Pourquoi elle est là |
|---|---|
| Liste vide ⇒ **tout refusé** | le défaut est le refus, jamais l'ouverture (ADR-017) |
| Contrôle sur le chemin **normalisé**, et l'appelant ouvre le chemin **retourné** | vérifier une chaîne puis en ouvrir une autre est le motif classique du contournement — `..\..\` |
| Comparaison **par segment** | `C:\Data` ne doit pas autoriser `C:\DataSecret` — un `StartsWith` nu se fait avoir |
| Résolution des **liens** | un raccourci déposé dans un dossier autorisé rouvrirait tout le disque |
| **Noms refusés** même sous une racine autorisée | autoriser « le dossier projet » reste correct : c'est le `.env` qui s'y trouve qui ne doit pas sortir |
| `.env*` par **préfixe** | `.env.local`, `.env.production` — les lister un par un garantirait d'en oublier un |
| Filtrage appliqué **aussi au listing** | révéler qu'un `.ssh` existe renseigne l'attaquant même sans en lire le contenu |
| `OrdinalIgnoreCase` | Windows est insensible à la casse : `.SSH` et `.ssh` sont le même dossier |

Noms refusés par défaut : `.ssh` · `.aws` · `.azure` · `.gnupg` · `.git` · `id_rsa` ·
`id_ed25519` · `id_ecdsa` · `credentials` · `secrets.json` · `.npmrc` · `.pypirc` ·
`appsettings.Production.json` · `appsettings.Development.json` · `.env*`

**Conséquence opérationnelle** : `AllowedRoots` étant vide par défaut, `read_file` et
`list_files` **refusent tout** tant que la configuration ne déclare pas de racine. C'est voulu —
et le message d'erreur nomme la clé à renseigner. À déclarer **étroit** : les dépôts de code et
les documents de travail, **pas** `C:\Users\<toi>`, qui contient `.ssh`, les cookies et les jetons.

16 tests dans `daemon/Orion.Daemon.Tests/PathScopeTests.cs`, écrits sur les **contournements**
plutôt que sur le cas passant : remontée par `..`, racine voisine au nom plus long, casse, nom
sensible enfoui, racine de volume, entrée blanche dans la liste.

**Ce que ça ne couvre pas** : `write_file` (C2) partage le même besoin mais n'est pas encore câblé
sur `PathScope` — il reste protégé par la seule confirmation.

---

### C2 — ✅ CORRIGÉ — Écriture du disque sans périmètre

**Où** : `daemon/Orion.Daemon.Actions/WriteFileAction.cs` · **Outil** : `write_file`

Même dette, côté écriture, avec `Directory.CreateDirectory` en prime : le chemin était normalisé
puis écrit, **n'importe où**, en créant l'arborescence au passage. Cible évidente : le dossier
Démarrage — or c'est précisément par là que le daemon lui-même est lancé (cf. [daemon.md](daemon.md)).
Un fichier déposé là s'exécute à la prochaine ouverture de session : **persistance complète**.

`IsDestructive == true` l'atténuait — mais la confirmation *affiche* un chemin, elle ne le
**valide** pas.

**Correctif** : le même `PathScope`, sur `DaemonOptions.AllowedWriteRoots`.

Écrire et lire ne sont **pas la même permission** : `AllowedWriteRoots` est donc distinct. Vide,
il retombe sur `AllowedRoots` — c'est-à-dire vers un ensemble **plus petit ou égal**, jamais vers
« tout ». Les deux listes vides refusent tout. La confirmation reste : elle ne remplace pas le
périmètre, elle s'y ajoute.

---

### E1 — ✅ CORRIGÉ — `run_script` : le guillemet cassait la commande

**Où** : `daemon/Orion.Daemon.Actions/RunScriptAction.cs` · **Outil** : `run_script`

```csharp
Arguments = $"-ExecutionPolicy Bypass -Command \"{script}\"",
```

`run_script` exécute du code arbitraire **par conception** — ce n'était pas le constat. Le constat
était que **l'échappement était absent** : un `script` contenant un guillemet double terminait
l'argument, et la suite était relue par `powershell.exe` comme **ses** options. La commande
réellement lancée cessait de correspondre à celle que l'utilisateur avait confirmée.

Une confirmation qui porte sur autre chose que ce qui s'exécute n'est pas une confirmation.

**Correctif** : `-EncodedCommand` (base64 d'UTF-16LE). Il n'y a alors plus la moindre frontière de
guillemet à casser, et ce qui s'exécute est exactement ce qui a été affiché. S'y ajoutent :

- `-NoProfile` — le profil de l'utilisateur pouvait redéfinir des commandes et changer le sens du
  script sans que rien ne l'indique ;
- `-NonInteractive` — un script qui pose une question restait bloqué à attendre une réponse que
  personne ne verrait jamais ;
- un **plafond de durée** (`Daemon:ScriptTimeoutSeconds`, 120 s par défaut) : le daemon traite les
  commandes une par une, donc un seul script suspendu suffisait à rendre ORION muet sur tout le
  reste, sans message ;
- lecture des deux flux **avant** l'attente : un script qui écrit plus que la taille du tampon de
  tube se bloquait en écriture pendant qu'on l'attendait — les deux camps s'attendaient
  indéfiniment.

---

### E2 — ✅ CORRIGÉ — SSRF : n'importe quelle URI était acceptée

**Où** : `WebFetchTool.cs`, `WebBrowseTool.cs`, `ScreenshotTool.cs`

```csharp
if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))   // seul contrôle
```

Absolue, et c'est tout. Ni schéma, ni hôte. `InternetOptions.BlockedDomains` existait —
`grep -rn "BlockedDomains" backend/ --include=*.cs` ne renvoyait **qu'une ligne : sa propre
déclaration**.

`ScreenshotTool` avait bien un garde, et c'était pire : une liste de sous-chaînes
(`banking`, `secure`, `login`, `auth`, `account`) cherchée dans l'URL en minuscules. Il refusait
une page Wikipédia dont le titre contient « Login », et laissait passer `http://169.254.169.254/`.
Bruyant sur le légitime, aveugle sur la menace — et c'était un **troisième** contrôle d'URL, plus
faible que les deux autres.

**Correctif** : `Orion.Business/Tools/Internet/UrlScope.cs`, **un seul** contrôle pour les trois
outils.

| Règle | Pourquoi |
|---|---|
| Liste **fermée** de schémas (`http`, `https`) | une liste d'interdits oublierait toujours le prochain — `file`, `data`, `gopher`… |
| Refus des adresses internes **après résolution DNS** | un nom parfaitement public peut pointer sur `127.0.0.1` : ne contrôler que la chaîne laisse passer exactement ce cas |
| **Toutes** les adresses résolues doivent être publiques | sinon un nom résolvant vers un mélange sert à atteindre le privé au gré de l'ordre |
| `BlockedDomains` enfin **lu**, par suffixe de domaine | `interdit.test` bloque `api.interdit.test` mais **pas** `pasinterdit.test` — la faille du `Contains()` |
| **Redirections suivies à la main**, chacune revalidée | sans ça le garde se contourne en une ligne : une URL publique répondant 302 vers `169.254.169.254` passait le contrôle d'entrée, et c'est la destination qui était lue. `AllowAutoRedirect = false` dans `Program.cs` |
| Filtre de **navigation** Playwright | même raison, côté navigateur : c'est lui qui suit les redirections |
| L'action `goto` de `web_browse` validée aussi | c'est une seconde porte d'entrée, avec une URL distincte de celle validée à l'ouverture |

Plages refusées : loopback, `10/8`, `172.16/12`, `192.168/16`, `169.254/16` (**les métadonnées
d'instance**, qui rendent des identifiants sans authentification), `100.64/10`, `0/8`, multicast,
et côté IPv6 lien-local, site-local et `fc00::/7` — y compris sous forme IPv4 mappée
(`::ffff:127.0.0.1`).

43 cas de test dans `backend/Orion.Tests/Tools/UrlScopeTests.cs`, dont les **bornes exactes** des
plages privées (`172.15` non, `172.16` oui, `172.31` oui, `172.32` non) : c'est là que ce genre de
classification se trompe.

**Résiduel assumé** : entre notre résolution DNS et celle du client HTTP, un nom peut changer de
réponse (*DNS rebinding*). Fermer ça imposerait de se connecter à l'IP validée en forçant l'en-tête
`Host` — non fait, et donc noté ici plutôt que passé sous silence.

---

### M1 — ✅ CORRIGÉ — `kill_process` tuait toute la famille

**Où** : `daemon/Orion.Daemon.Actions/KillProcessAction.cs`

`Process.GetProcessesByName(name)` puis `Kill(entireProcessTree: true)` **sur chaque résultat**.
`kill_process("chrome")` fermait toutes les fenêtres du navigateur, pas une — alors que la
confirmation n'annonçait qu'un nom. L'utilisateur validait autre chose que ce qui allait se produire.

**Correctif** : quand un nom correspond à plusieurs processus, l'action **refuse et les énumère**
(nom + PID) au lieu d'agir large en silence. Le modèle peut alors viser un `pid` précis, ou
redemander explicitement avec `all: true` — paramètre ajouté au schéma de `KillProcessTool`, avec
une description qui dit ce qu'il déclenche. Un seul processus correspond : rien ne change.

---

### M2 — ✅ CORRIGÉ — `ReadFileAction` lisait le fichier trois fois

`File.ReadLines(fullPath)` était appelé **trois fois** (contenu, `totalLines`, `truncated`) : trois
parcours disque, et trois instants différents — un fichier qui change entre les deux donnait un
`truncated` incohérent. Corrigé en même temps que C1, l'action ayant été réécrite : une seule
matérialisation, tout se calcule dessus.

### M3 — ✅ CORRIGÉ — `-ExecutionPolicy Bypass` dans la doc d'installation

`docs/daemon.md` documente l'installation via `powershell -ExecutionPolicy Bypass -File ...`. C'est
courant et ici assumé (script local, non élevé), mais cela entraîne l'utilisateur à contourner la
politique par réflexe. Corrigé : `docs/daemon.md` documente désormais `Unblock-File`.

---

## 4. Tableau de bord

| # | Constat | État | Où vit le garde |
|---|---|---|---|
| C1 | `read_file` / `list_files` — tout le disque | ✅ corrigé | `PathScope` · `AllowedRoots` |
| C2 | `write_file` — tout le disque | ✅ corrigé | `PathScope` · `AllowedWriteRoots` |
| E1 | `run_script` — échappement absent | ✅ corrigé | `-EncodedCommand` |
| E2 | `web_fetch` / `web_browse` / `screenshot_page` — SSRF | ✅ corrigé | `UrlScope` |
| M1 | `kill_process` — N processus pour un nom | ✅ corrigé | refus + énumération |
| M2 | `ReadFileAction` — triple lecture disque | ✅ corrigé | une seule matérialisation |
| M3 | `Bypass` enseigné par la doc | ✅ corrigé | `Unblock-File` documenté |

**Les sept constats de l'audit sont fermés.** Le fil rouge du §3 l'est aussi : `DaemonOptions` et
`InternetOptions.BlockedDomains` sont désormais **lus** — plus aucune option ne se fait passer pour
une défense.

### Ce qui reste ouvert, et qui n'était pas dans l'audit

Signalé plutôt que tu, parce qu'un correctif ne vaut que ce que vaut sa liste de trous restants :

- **DNS rebinding** — entre notre résolution et celle du client HTTP, un nom peut changer de
  réponse. Fermer ça imposerait de se connecter à l'IP validée en forçant l'en-tête `Host`.
- **`workingDir` de `run_script`** n'est pas soumis à un périmètre. C'est assumé : le script étant
  arbitraire, il peut se déplacer où il veut — un contrôle là serait du théâtre.
- **Sous-ressources de `web_browse`** (images, feuilles de style) ne sont pas filtrées : seules les
  navigations le sont. Une sous-ressource n'est pas relue et ne repart pas vers le modèle.
- **Périmètre côté BACKEND** : les outils `read_file` / `write_file` transmettent le chemin tel
  quel ; le refus vient du daemon. C'est le bon endroit — c'est lui qui touche le disque — mais le
  modèle ne sait donc qu'il a franchi une limite qu'**après** l'aller-retour.

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
