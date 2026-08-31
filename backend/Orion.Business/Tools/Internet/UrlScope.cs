using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;

namespace Orion.Business.Tools.Internet;

/// <summary>
/// Le PÉRIMÈTRE réseau des outils qui vont chercher une page — constat E2 de l'audit du
/// 2026-08-27.
///
/// AVANT. `web_fetch` et `web_browse` ne vérifiaient qu'une chose :
/// <c>Uri.TryCreate(url, UriKind.Absolute, out _)</c>. Ni schéma, ni hôte. `BlockedDomains`
/// était déclaré dans <see cref="InternetOptions"/> et <b>lu par personne</b> — un garde-fou
/// qui n'existait que dans la configuration.
///
/// Étaient donc atteignables : <c>http://127.0.0.1:5107/api/…</c> (l'API elle-même, en loopback,
/// où le filtrage d'origine ne s'applique pas), <c>http://169.254.169.254/</c> (métadonnées
/// d'instance sur un VPS cloud), et <c>file:///</c> selon le handler.
///
/// Ces deux outils ne sont pas destructifs : ils partent SANS confirmation. Et ce sont eux qui
/// font entrer du texte étranger dans le contexte du modèle — c'est-à-dire le vecteur d'injection
/// de prompt que `ToolInvoker` documente déjà. Le garde doit donc vivre ici.
/// </summary>
public sealed class UrlScope
{
    private readonly InternetOptions _options;

    public UrlScope(IOptions<InternetOptions> options) => _options = options.Value;

    /// <summary>
    /// L'URI si elle est autorisée, sinon <c>null</c> et <paramref name="raison"/> dit pourquoi.
    ///
    /// La résolution DNS est faite ICI, avant la requête : un nom public peut pointer sur
    /// 127.0.0.1, et ne contrôler que la chaîne laisserait passer exactement ce cas.
    /// </summary>
    public async Task<Uri?> ResoudreAsync(string? url, CancellationToken ct = default)
        => (await VerifierAsync(url, ct)).Uri;

    public async Task<(Uri? Uri, string Raison)> VerifierAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (null, "URL vide.");

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return (null, "URL invalide.");

        // Liste FERMÉE de schémas. Une liste de schémas interdits oublierait toujours le
        // prochain (file, gopher, ftp, data…) ; celle-ci ne laisse passer que ce qui est voulu.
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return (null, $"Schéma « {uri.Scheme} » refusé — seuls http et https sont autorisés.");

        var hote = uri.DnsSafeHost;
        if (string.IsNullOrWhiteSpace(hote))
            return (null, "URL sans hôte.");

        if (EstDomaineBloque(hote))
            return (null, $"Domaine « {hote} » refusé (Internet:BlockedDomains).");

        // Hôte écrit directement en IP : pas de DNS à interroger, mais le même contrôle.
        if (IPAddress.TryParse(hote, out var litterale))
        {
            return EstInterne(litterale)
                ? (null, $"Adresse interne « {litterale} » refusée — ORION ne sonde pas le réseau local.")
                : (uri, string.Empty);
        }

        IPAddress[] adresses;
        try
        {
            adresses = await Dns.GetHostAddressesAsync(hote, ct);
        }
        catch (Exception ex)
        {
            return (null, $"Hôte « {hote} » irrésolvable : {ex.Message}");
        }

        if (adresses.Length == 0)
            return (null, $"Hôte « {hote} » ne résout vers aucune adresse.");

        // TOUTES doivent être publiques, pas seulement la première : un nom qui résout vers un
        // mélange public/privé servirait à atteindre le privé au gré de l'ordre de résolution.
        var interne = adresses.FirstOrDefault(EstInterne);
        if (interne is not null)
            return (null, $"« {hote} » résout vers l'adresse interne {interne} — refusé.");

        return (uri, string.Empty);
    }

    private bool EstDomaineBloque(string hote)
        => (_options.BlockedDomains ?? Array.Empty<string>())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim().TrimStart('.'))
            .Any(d => hote.Equals(d, StringComparison.OrdinalIgnoreCase)
                   || hote.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Loopback, réseaux privés, lien-local et compagnie. Le cas qui compte vraiment est
    /// <c>169.254.169.254</c> : sur la plupart des hébergeurs, c'est le service de métadonnées
    /// d'instance, et il rend des identifiants sans demander la moindre authentification.
    /// </summary>
    public static bool EstInterne(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip) || IPAddress.Any.Equals(ip) || IPAddress.IPv6Any.Equals(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var o = ip.GetAddressBytes();
            return o[0] == 0                                    // 0.0.0.0/8   — « ce réseau »
                || o[0] == 10                                   // 10.0.0.0/8
                || o[0] == 127                                  // loopback
                || (o[0] == 100 && o[1] >= 64 && o[1] <= 127)   // 100.64/10   — CGNAT
                || (o[0] == 169 && o[1] == 254)                 // 169.254/16  — lien-local + MÉTADONNÉES
                || (o[0] == 172 && o[1] >= 16 && o[1] <= 31)    // 172.16/12
                || (o[0] == 192 && o[1] == 0 && o[2] == 0)      // 192.0.0/24  — IETF
                || (o[0] == 192 && o[1] == 168)                 // 192.168/16
                || (o[0] == 198 && (o[1] & 0xFE) == 18)         // 198.18/15   — bancs d'essai
                || o[0] >= 224;                                 // multicast + réservé
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
                return true;

            // fc00::/7 — adresses locales uniques.
            return (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        // Famille inattendue : on refuse. Ne jamais élargir sur l'inconnu.
        return true;
    }
}
