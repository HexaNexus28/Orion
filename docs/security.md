# Sécurité

## Signaler une vulnérabilité

Merci de ne pas ouvrir d'issue publique. Utiliser le signalement privé de GitHub, depuis l'onglet
Security du dépôt.

## Modèle de menace

ORION agit sur un poste avec les droits de son propriétaire, et il lit du contenu externe. Une page
web peut contenir des instructions destinées au modèle : c'est l'injection de prompt. Une requête
ainsi détournée reste authentifiée, le contrôle d'accès ne peut donc pas l'arrêter.

Les protections s'appliquent pour cette raison à deux niveaux :

1. Qui peut appeler ORION : authentification.
2. Ce qu'une décision du modèle peut déclencher : contrôles dans le code, après cette décision.

## Contrôles d'accès

| Contrôle | Emplacement |
|---|---|
| Routes fermées par défaut (`FallbackPolicy`) | `Program.cs` |
| Refus si un secret est absent | `DaemonAuthenticationHandler`, `AuthController` |
| Comparaison à temps constant des secrets | `AuthController`, `DaemonAuthenticationHandler` |
| Limitation des échecs de connexion | `AuthController` |
| Billet de flux de 60 s à audience distincte | `AuthController`, `Program.cs` |
| Origines WebSocket obligatoires | `Program.cs` |

## Contrôles des actions

### Confirmation

Toute action `IsDestructive` passe par `IToolInvoker`, qui la met en attente de confirmation. Le
modèle ne dispose d'aucun outil pour confirmer lui-même.

### Disque

`PathScope` protège `read_file`, `list_files` et `write_file`.

- Une liste de racines vide refuse tout.
- Le contrôle porte sur le chemin normalisé, et l'action ouvre le chemin retourné.
- La comparaison se fait par segment de chemin.
- Les liens sont résolus.
- Les noms sensibles sont refusés même sous une racine autorisée, y compris dans les listings.
- Lecture et écriture ont des listes distinctes.

### Réseau

`UrlScope` protège `web_fetch`, `web_browse` et `screenshot_page`.

- Seuls `http` et `https` sont acceptés.
- Les adresses internes sont refusées après résolution DNS.
- Toutes les adresses résolues doivent être publiques.
- `BlockedDomains` s'applique par suffixe de domaine.
- Chaque redirection est revalidée, et les navigations Playwright sont filtrées.

### Processus

- `run_script` utilise `-EncodedCommand`, `-NoProfile` et `-NonInteractive`, avec une durée maximale.
- `kill_process` refuse un nom ambigu.
- `DaemonActionValidator` limite les actions disponibles.

## Secrets

Les secrets arrivent par variables d'environnement et ne sont pas versionnés. Voir
[deployment.md](deployment.md).

## Données transmises à des tiers

| Donnée | Destinataire |
|---|---|
| Audio, transcriptions, texte des réponses vocales, contenu à vectoriser | Mistral |
| Conversations, profil et souvenirs inclus dans le prompt | fournisseur LLM configuré |
| Données persistées | base PostgreSQL configurée |

Avant tout usage avec des données personnelles, vérifier les conditions de chaque fournisseur :
réutilisation des données, durée de conservation et localisation.

## Avant d'exposer une instance

- Déclarer des racines `AllowedRoots` étroites.
- Utiliser des secrets forts et distincts.
- Limiter `AllowedOrigins` au domaine public.
- Vérifier le masquage des jetons dans les logs de Nginx.
