namespace Orion.Api.Authentication;

/// <summary>
/// Modele d'authentification d'ORION — une seule porte, deux appelants.
///
/// HISTORIQUE. La meme question — « qui appelle ? » — recevait quatre reponses differentes :
/// JWT sur les controleurs, secret partage compare a la main dans le middleware daemon, RIEN
/// sur le WebSocket voix, RIEN sur les appels HTTP du daemon. Chaque trou se corrigeait par un
/// controle de plus, ecrit a un endroit de plus. Ce fichier existe pour que cette question ait
/// UNE reponse, a UN endroit.
///
/// LES DEUX APPELANTS, et rien d'autre :
///   - le proprietaire (navigateur, n'importe quel appareil) → JWT signe, obtenu par mot de
///     passe. Un navigateur ne peut rien garder de permanent : un secret dans le bundle serait
///     lisible par quiconque charge la page.
///   - le daemon (machine de confiance) → secret partage. Aucun login interactif possible sur
///     un service Windows.
///
/// CE QUI DECOULE DU TRANSPORT, pas d'un choix :
///   HTTP porte un en-tete. EventSource et WebSocket cote NAVIGATEUR n'en portent aucun —
///   c'est une limite des navigateurs. Le daemon, lui, n'est pas un navigateur : il porte son
///   en-tete partout, y compris sur son WebSocket. D'ou l'asymetrie ci-dessous, qui n'est pas
///   une incoherence mais la consequence directe de qui parle par quel tuyau.
/// </summary>
public static class OrionAuth
{
    /// <summary>
    /// Schema d'aiguillage, et schema PAR DEFAUT de l'application.
    ///
    /// Indispensable : `UseAuthentication()` n'execute QUE le schema par defaut. Declarer un
    /// schema « Daemon » sans lui ne servirait a rien — il ne serait jamais invoque, et
    /// `context.User` resterait vide pour le daemon. Ce selecteur regarde la requete et
    /// delegue au bon schema. C'est la piece qui rend le modele unique au lieu d'empile.
    /// </summary>
    public const string SelectorScheme = "OrionSelector";

    public const string DaemonScheme = "Daemon";
    public const string DaemonTokenHeader = "X-Daemon-Token";

    public const string OwnerRole = "owner";
    public const string DaemonRole = "daemon";

    /// <summary>Politique par defaut de toute l'API. Le daemon ne la satisfait PAS.</summary>
    public const string OwnerPolicy = "Owner";

    /// <summary>Reservee aux deux routes que le daemon appelle vraiment.</summary>
    public const string DaemonPolicy = "DaemonOnly";

    /// <summary>
    /// Les SEULS chemins ou le jeton a le droit de voyager dans l'URL.
    ///
    /// Une URL se retrouve dans les journaux d'acces du serveur, dans l'historique du
    /// navigateur et dans l'en-tete Referer. On ne paie ce prix que la ou il n'existe aucune
    /// alternative : SSE et WebSocket navigateur ne peuvent porter aucun en-tete. Toute autre
    /// route qui accepterait `?access_token=` serait une fuite gratuite — d'ou cette liste
    /// FERMEE, seul endroit a modifier si un nouveau flux de ce type apparait.
    /// </summary>
    public static readonly string[] QueryTokenPaths =
    {
        "/api/proactivenotification/stream",
        "/ws/voice"
    };

    public static bool AllowsQueryToken(PathString path) =>
        QueryTokenPaths.Any(p => path.StartsWithSegments(p));
}

/// <summary>
/// Garde partage des middlewares WebSocket.
///
/// Un middleware n'est PAS un endpoint : la `FallbackPolicy` ne l'atteint pas. Sans ce garde,
/// chaque WebSocket devrait reecrire son propre controle — et c'est exactement comme ca que
/// /ws/voice s'est retrouve sans aucune verification pendant que /daemon en avait une.
/// </summary>
public static class WebSocketAuthGuard
{
    /// <summary>
    /// Retourne false et ECRIT la reponse si l'appelant n'a pas le role demande.
    /// A appeler AVANT AcceptWebSocketAsync : apres l'upgrade, plus aucun code HTTP n'est lisible.
    /// </summary>
    public static bool Require(HttpContext context, string role, ILogger logger, string canal)
    {
        var authenticated = context.User.Identity?.IsAuthenticated == true;

        if (authenticated && context.User.IsInRole(role))
            return true;

        // 401 = « je ne sais pas qui tu es », 403 = « je sais, et tu n'as pas le droit ».
        // La distinction compte cote client : le premier doit declencher une reconnexion,
        // le second non — une reconnexion en boucle sur un 403 ne se resoudrait jamais.
        context.Response.StatusCode = authenticated ? 403 : 401;
        logger.LogWarning("[{Canal}] Connexion refusee ({Code}) depuis {Ip}",
            canal, context.Response.StatusCode, context.Connection.RemoteIpAddress);
        return false;
    }
}
