namespace Orion.Api.Authentication;

/// <summary>
/// Modele d'authentification d'ORION : une porte, deux appelants — le proprietaire (JWT obtenu
/// par mot de passe) et le daemon (secret partage). Voir docs/security.md et ADR-016.
///
/// L'asymetrie des transports en decoule : un navigateur ne peut porter d'en-tete ni sur SSE ni
/// sur WebSocket, le daemon si.
/// </summary>
public static class OrionAuth
{
    /// <summary>
    /// Schema d'aiguillage, et schema PAR DEFAUT — indispensable : `UseAuthentication()`
    /// n'execute QUE le schema par defaut, donc un schema « Daemon » declare seul ne serait
    /// jamais invoque.
    /// </summary>
    public const string SelectorScheme = "OrionSelector";

    public const string DaemonScheme = "Daemon";
    public const string DaemonTokenHeader = "X-Daemon-Token";

    public const string Issuer = "orion";

    /// <summary>Audience des jetons de session (en-tete Authorization).</summary>
    public const string Audience = "orion";

    /// <summary>
    /// Audience des BILLETS DE FLUX. Distincte, et pas seulement courte : c'est elle qui rend
    /// les deux jetons non interchangeables dans les DEUX sens. Raccourcir la duree seule ne
    /// fermerait rien.
    /// </summary>
    public const string StreamAudience = "orion-stream";

    /// <summary>60 s : le billet n'ouvre que la connexion, il ne la maintient pas.</summary>
    public const int StreamTicketLifetimeSeconds = 60;

    /// <summary>
    /// Marque, posee sur la requete, indiquant que le jeton vient de l URL et non d un en-tete.
    /// C est ce qui permet de refuser un jeton de session presente dans une URL.
    /// </summary>
    public const string TokenFromUrl = "orion.jeton.url";

    public const string OwnerRole = "owner";
    public const string DaemonRole = "daemon";

    /// <summary>Politique par defaut de toute l'API. Le daemon ne la satisfait PAS.</summary>
    public const string OwnerPolicy = "Owner";

    /// <summary>Reservee aux deux routes que le daemon appelle vraiment.</summary>
    public const string DaemonPolicy = "DaemonOnly";

    /// <summary>
    /// Les SEULS chemins ou le jeton a le droit de voyager dans l'URL.
    ///
    /// Liste FERMEE : une URL finit dans les journaux et le Referer, on ne paie ce prix que la
    /// ou aucun en-tete n'est possible. Seul endroit a modifier pour un nouveau flux.
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
/// Garde partage des middlewares WebSocket : un middleware n'est PAS un endpoint, la
/// `FallbackPolicy` ne l'atteint donc pas.
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

        // 401 declenche une reconnexion cote client, 403 non : une boucle sur un 403 ne se
        // resoudrait jamais.
        context.Response.StatusCode = authenticated ? 403 : 401;
        logger.LogWarning("[{Canal}] Connexion refusee ({Code}) depuis {Ip}",
            canal, context.Response.StatusCode, context.Connection.RemoteIpAddress);
        return false;
    }
}
