namespace Orion.Daemon.Core.Security;

/// <summary>
/// Le PÉRIMÈTRE des actions qui touchent au disque — constat C1 de l'audit du 2026-08-27.
///
/// POURQUOI ICI. `ReadFileAction` et `ListFilesAction` recevaient `DaemonOptions` par injection
/// et ne s'en servaient JAMAIS : un lecteur croyait voir un garde-fou configurable, il n'y en
/// avait aucun. `Path.GetFullPath(path)` puis accès direct — tout le disque, y compris
/// `%USERPROFILE%\.ssh\id_rsa`, les `.env` de n'importe quel projet, et
/// `appsettings.Production.json` du daemon (donc le jeton du canal lui-même).
///
/// Ce que ça ferme, et pourquoi ça compte : `read_file` et `list_files` ne sont PAS destructifs,
/// donc ils ne passent pas par la file de confirmation — ils partent immédiatement. Or ORION lit
/// le web : une page peut détourner le modèle, et la requête résultante est parfaitement
/// AUTHENTIFIÉE. Le contenu lu revient au modèle, qui le restitue dans sa réponse : la lecture
/// EST l'exfiltration. Aucun contrôle d'accès ne peut arrêter ça — seul un périmètre le peut.
///
/// LE DÉFAUT EST LE REFUS. Liste vide = rien n'est autorisé, jamais « tout est autorisé ».
/// C'est le défaut exact qui rendait le WebSocket daemon libre quand la variable manquait.
/// </summary>
public sealed class PathScope
{
    /// <summary>
    /// Refusés même SOUS une racine autorisée. Autoriser « le dossier projet » reste correct :
    /// c'est le `.env` qui s'y trouve qui ne doit pas sortir. Une racine autorisée dit où
    /// chercher, pas que tout ce qu'elle contient est anodin.
    /// </summary>
    public static readonly string[] DefaultDeniedNames =
    {
        ".ssh", ".aws", ".azure", ".gnupg", ".git",
        "id_rsa", "id_ed25519", "id_ecdsa",
        "credentials", "secrets.json", ".npmrc", ".pypirc",
        "appsettings.Production.json", "appsettings.Development.json",
    };

    private readonly string[] _roots;
    private readonly HashSet<string> _deniedNames;

    public PathScope(IEnumerable<string>? allowedRoots, IEnumerable<string>? deniedNames = null)
    {
        _roots = (allowedRoots ?? Enumerable.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(Normaliser)
            .ToArray();

        _deniedNames = new HashSet<string>(
            deniedNames?.Any() == true ? deniedNames : DefaultDeniedNames,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Aucune racine déclarée : tout est refusé, et l'appelant doit le dire clairement.</summary>
    public bool EstConfigure => _roots.Length > 0;

    /// <summary>
    /// Le chemin normalisé si l'accès est autorisé, sinon <c>null</c> et <paramref name="raison"/>
    /// explique quoi corriger.
    ///
    /// Renvoie le chemin RÉSOLU pour que l'appelant travaille sur celui-là et pas sur l'entrée :
    /// vérifier une chaîne puis en ouvrir une autre est le motif classique du contournement.
    /// </summary>
    public string? Resoudre(string? chemin, out string raison)
    {
        if (string.IsNullOrWhiteSpace(chemin))
        {
            raison = "Chemin vide.";
            return null;
        }

        if (!EstConfigure)
        {
            raison = "Aucune racine autorisée n'est configurée (Daemon:AllowedRoots) — "
                   + "accès refusé par défaut. Déclarer les dossiers qu'ORION a le droit de lire.";
            return null;
        }

        string complet;
        try
        {
            // NORMALISER D'ABORD. Comparer la chaîne d'entrée laisserait passer « ..\..\ » :
            // le contrôle porterait sur un texte, l'ouverture sur un autre chemin.
            complet = Normaliser(chemin);
        }
        catch (Exception ex)   // chemin syntaxiquement invalide, trop long, caractères interdits
        {
            raison = $"Chemin invalide : {ex.Message}";
            return null;
        }

        // Un lien peut pointer HORS du périmètre : sans cette résolution, un raccourci déposé
        // dans un dossier autorisé rouvrirait tout le disque. On vérifie la cible réelle.
        complet = ResoudreLien(complet);

        var racine = _roots.FirstOrDefault(r => EstSousRacine(complet, r));
        if (racine is null)
        {
            raison = $"Chemin hors périmètre : « {complet} » n'est sous aucune racine autorisée.";
            return null;
        }

        // Le nom refusé peut être n'importe où dans la descente : `projet/.ssh/id_rsa` doit
        // tomber sur `.ssh`, pas seulement sur le dernier segment.
        var segmentRefuse = SegmentsSous(complet, racine).FirstOrDefault(EstNomRefuse);
        if (segmentRefuse is not null)
        {
            raison = $"Accès refusé : « {segmentRefuse} » est un emplacement sensible, "
                   + "même sous une racine autorisée.";
            return null;
        }

        raison = string.Empty;
        return complet;
    }

    /// <summary>Filtre une liste d'entrées — un listing ne doit pas révéler ce qu'il refuse de lire.</summary>
    public bool EstVisible(string cheminComplet)
        => Resoudre(cheminComplet, out _) is not null;

    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static string Normaliser(string chemin)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(chemin.Trim()));

    private static string ResoudreLien(string chemin)
    {
        try
        {
            var cible = Directory.Exists(chemin)
                ? Directory.ResolveLinkTarget(chemin, returnFinalTarget: true)?.FullName
                : File.ResolveLinkTarget(chemin, returnFinalTarget: true)?.FullName;

            return cible is null ? chemin : Path.TrimEndingDirectorySeparator(cible);
        }
        catch
        {
            // Cible cassée ou illisible : on garde le chemin normalisé, qui sera de toute façon
            // confronté aux racines. Ne jamais élargir sur une exception.
            return chemin;
        }
    }

    /// <summary>
    /// Comparaison par SEGMENT, jamais par simple préfixe de chaîne : « C:\Data » ne doit pas
    /// autoriser « C:\DataSecret ». Le séparateur ajouté est ce qui fait la différence.
    ///
    /// OrdinalIgnoreCase : le daemon ne tourne que sur Windows, dont le système de fichiers est
    /// insensible à la casse. Une comparaison sensible y laisserait passer « c:\data\.SSH ».
    /// </summary>
    private static bool EstSousRacine(string chemin, string racine)
        => chemin.Equals(racine, StringComparison.OrdinalIgnoreCase)
        || chemin.StartsWith(AvecSeparateurFinal(racine), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Une racine de volume (« C:\ », « / ») porte DÉJÀ son séparateur : lui en concaténer un
    /// second produirait « C:\\ », que plus aucun chemin ne préfixe — le périmètre deviendrait
    /// silencieusement infranchissable.
    /// </summary>
    private static string AvecSeparateurFinal(string racine)
        => racine.EndsWith(Path.DirectorySeparatorChar) || racine.EndsWith(Path.AltDirectorySeparatorChar)
            ? racine
            : racine + Path.DirectorySeparatorChar;

    /// <summary>
    /// Découpe par <c>GetRelativePath</c> plutôt que par arithmétique sur les longueurs : sur une
    /// racine de volume, un « + 1 » pour sauter le séparateur mange le premier caractère du nom
    /// et « .ssh » devient « ssh » — le filtre ne reconnaît plus rien.
    /// </summary>
    private static IEnumerable<string> SegmentsSous(string chemin, string racine)
        => Path.GetRelativePath(racine, chemin)
            .Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".");

    /// <summary>
    /// `.env` a autant de variantes que de projets (`.env.local`, `.env.production`) : les lister
    /// une par une garantirait d'en oublier une. Le préfixe les prend toutes.
    /// </summary>
    private bool EstNomRefuse(string segment)
        => _deniedNames.Contains(segment)
        || segment.StartsWith(".env", StringComparison.OrdinalIgnoreCase);
}
