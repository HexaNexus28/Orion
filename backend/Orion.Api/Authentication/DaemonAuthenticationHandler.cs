using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;

namespace Orion.Api.Authentication;

/// <summary>
/// Authentifie le daemon par secret partage, et lui donne une IDENTITE.
///
/// POURQUOI un schema plutot qu'un `if` dans le middleware. Avant, le WebSocket daemon
/// comparait le jeton a la main, et ses appels HTTP n'etaient pas verifies du tout — il
/// suffisait d'ajouter un appel pour creer un trou. Ici le daemon devient un ClaimsPrincipal
/// comme n'importe quel autre appelant : la meme `FallbackPolicy` s'applique a lui, et son
/// role limite ce qu'il peut atteindre. Le controle ne peut plus etre « oublie » quelque part.
/// </summary>
public class DaemonAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AuthOptions _auth;

    public DaemonAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<AuthOptions> auth)
        : base(options, logger, encoder)
    {
        _auth = auth.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Headers[OrionAuth.DaemonTokenHeader].FirstOrDefault();

        // Pas d'en-tete : ce n'est pas un echec, c'est « ce n'est pas moi ». NoResult laisse la
        // main au reste de la chaine. Un Fail() ici transformerait toute requete utilisateur
        // normale en erreur d'authentification.
        if (string.IsNullOrEmpty(presented))
            return Task.FromResult(AuthenticateResult.NoResult());

        // FAIL-CLOSED. Un jeton non configure REFUSE, il n'ouvre pas. C'est le defaut exact qui
        // rendait le WebSocket daemon librement accessible quand la variable d'environnement
        // manquait — sur un canal qui fait executer des actions sur la machine de l'utilisateur.
        if (string.IsNullOrEmpty(_auth.DaemonToken))
        {
            Logger.LogError("[Daemon] Jeton non configure — acces REFUSE. " +
                "Definir DAEMON_WS_TOKEN avant de demarrer en production.");
            return Task.FromResult(AuthenticateResult.Fail("Daemon token not configured"));
        }

        // Comparaison a temps constant : un `!=` classique s'arrete au premier octet different,
        // ce qui laisse mesurer le secret octet par octet.
        var expected = Encoding.UTF8.GetBytes(_auth.DaemonToken);
        var given = Encoding.UTF8.GetBytes(presented);
        if (given.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(given, expected))
        {
            Logger.LogWarning("[Daemon] Jeton invalide depuis {Ip}", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid daemon token"));
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, "orion-daemon"),
                new Claim(ClaimTypes.Role, OrionAuth.DaemonRole)
            },
            OrionAuth.DaemonScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), OrionAuth.DaemonScheme)));
    }
}
