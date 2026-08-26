namespace Orion.Core.Configuration;

/// <summary>
/// Authentification d'ORION — mot de passe unique, jeton JWT en echange.
///
/// POURQUOI. Jusqu'ici l'API n'avait AUCUNE protection : `app.UseAuthorization()` etait
/// commentee, aucun `[Authorize]`, aucun middleware d'authentification. Expose publiquement,
/// n'importe qui pouvait lire la memoire, parler a l'assistant, et surtout METTRE DES ACTIONS
/// EN FILE — actions qui s'executent ensuite sur la machine de l'utilisateur au reveil du
/// daemon. Ce n'etait pas une fuite de donnees, c'etait une execution de code a distance.
///
/// POURQUOI PAS UN SIMPLE SECRET PARTAGE. La PWA tourne dans un navigateur : un secret dans
/// son bundle serait lisible par quiconque charge la page. Un mot de passe echange contre un
/// jeton de session resout exactement ce probleme — rien de permanent ne vit cote client.
///
/// Le daemon garde son `X-Daemon-Token`, mais il n.est plus compare a la main dans un
/// middleware : il passe par un schema d.authentification a part entiere (OrionAuth.DaemonScheme)
/// et devient une identite avec un role. Voir Orion.Api/Authentication/OrionAuth.cs.
/// </summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Mot de passe unique. Vit dans le vault / une variable d'environnement, JAMAIS en clair dans un fichier suivi.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Cle de signature des jetons. 32 caracteres minimum (HMAC-SHA256).</summary>
    public string JwtSecret { get; set; } = string.Empty;

    /// <summary>
    /// Secret partage du daemon. Aucun login interactif n.est possible sur un service Windows,
    /// donc pas de JWT pour lui. Alimente depuis DAEMON_WS_TOKEN au demarrage (cf. Program.cs) :
    /// le nom de la variable reste celui deploye par Ansible, mais il n.est lu QU.UNE fois, ici.
    /// </summary>
    public string DaemonToken { get; set; } = string.Empty;

    /// <summary>30 jours : c'est un assistant personnel consulte depuis un telephone, pas une banque.</summary>
    public int TokenLifetimeDays { get; set; } = 30;

    /// <summary>
    /// FAIL-CLOSED : si la configuration est absente ou trop faible, l'API refuse TOUT au lieu
    /// de s'ouvrir. Un oubli de configuration doit fermer la porte, jamais l'ouvrir.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Password) && (JwtSecret?.Length ?? 0) >= 32;
}
