namespace Orion.Daemon.Core.Security;

/// <summary>
/// Périmètre des actions qui touchent au disque (constat C1, docs/security.md).
///
/// `read_file` et `list_files` ne sont pas destructifs : ils ne passent par aucune
/// confirmation. Or ORION lit le web, une page peut détourner le modèle, et ce qui est lu
/// revient dans sa réponse — la lecture EST l'exfiltration. Seul un périmètre l'arrête.
///
/// LE DÉFAUT EST LE REFUS : liste vide = rien n'est autorisé.
/// </summary>
public sealed class PathScope
{
    /// <summary>
    /// Refusés même SOUS une racine autorisée : une racine dit où chercher, pas que tout ce
    /// qu'elle contient est anodin.
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
            .Select(Normalize)
            .ToArray();

        _deniedNames = new HashSet<string>(
            deniedNames?.Any() == true ? deniedNames : DefaultDeniedNames,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Aucune racine déclarée : tout est refusé, et l'appelant doit le dire clairement.</summary>
    public bool IsConfigured => _roots.Length > 0;

    /// <summary>
    /// Le chemin RÉSOLU si l'accès est autorisé, sinon <c>null</c> et <paramref name="reason"/>.
    /// L'appelant doit ouvrir celui-là, pas son entrée : vérifier une chaîne puis en ouvrir une
    /// autre est le motif classique du contournement.
    /// </summary>
    public string? Resolve(string? path, out string reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "Chemin vide.";
            return null;
        }

        if (!IsConfigured)
        {
            reason = "Aucune racine autorisée n'est configurée (Daemon:AllowedRoots) — "
                   + "accès refusé par défaut. Déclarer les dossiers qu'ORION a le droit de lire.";
            return null;
        }

        string fullPath;
        try
        {
            // Normaliser D'ABORD : comparer l'entrée brute laisserait passer « ..\..\ ».
            fullPath = Normalize(path);
        }
        catch (Exception ex)   // chemin invalide, trop long, caractères interdits
        {
            reason = $"Chemin invalide : {ex.Message}";
            return null;
        }

        // Un raccourci déposé dans un dossier autorisé rouvrirait tout le disque.
        fullPath = ResolveLink(fullPath);

        var root = _roots.FirstOrDefault(r => IsUnderRoot(fullPath, r));
        if (root is null)
        {
            reason = $"Chemin hors périmètre : « {fullPath} » n'est sous aucune racine autorisée.";
            return null;
        }

        // Le nom refusé peut être n'importe où dans la descente, pas seulement en dernier.
        var deniedSegment = SegmentsUnder(fullPath, root).FirstOrDefault(IsDeniedName);
        if (deniedSegment is not null)
        {
            reason = $"Accès refusé : « {deniedSegment} » est un emplacement sensible, "
                   + "même sous une racine autorisée.";
            return null;
        }

        reason = string.Empty;
        return fullPath;
    }

    /// <summary>Un listing ne doit pas révéler ce qu'il refuse de lire.</summary>
    public bool IsVisible(string fullPath)
        => Resolve(fullPath, out _) is not null;

    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static string Normalize(string chemin)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(chemin.Trim()));

    private static string ResolveLink(string chemin)
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
    /// Par SEGMENT, jamais par préfixe de chaîne : « C:\Data » ne doit pas autoriser
    /// « C:\DataSecret ». OrdinalIgnoreCase car Windows ignore la casse.
    /// </summary>
    private static bool IsUnderRoot(string chemin, string racine)
        => chemin.Equals(racine, StringComparison.OrdinalIgnoreCase)
        || chemin.StartsWith(WithTrailingSeparator(racine), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Une racine de volume porte DÉJÀ son séparateur : en ajouter un second produirait
    /// « C:\\ », que plus aucun chemin ne préfixe — périmètre silencieusement infranchissable.
    /// </summary>
    private static string WithTrailingSeparator(string racine)
        => racine.EndsWith(Path.DirectorySeparatorChar) || racine.EndsWith(Path.AltDirectorySeparatorChar)
            ? racine
            : racine + Path.DirectorySeparatorChar;

    /// <summary>
    /// <c>GetRelativePath</c> plutôt qu'une arithmétique sur les longueurs : un « + 1 » pour
    /// sauter le séparateur mange le premier caractère et « .ssh » devient « ssh ».
    /// </summary>
    private static IEnumerable<string> SegmentsUnder(string chemin, string racine)
        => Path.GetRelativePath(racine, chemin)
            .Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".");

    /// <summary>Le préfixe `.env` les prend toutes : les lister une à une en oublierait.</summary>
    private bool IsDeniedName(string segment)
        => _deniedNames.Contains(segment)
        || segment.StartsWith(".env", StringComparison.OrdinalIgnoreCase);
}
