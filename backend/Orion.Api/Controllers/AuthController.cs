using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Orion.Api.Authentication;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Responses;

namespace Orion.Api.Controllers;

public record LoginRequest(string Password);
public record LoginResponse(string Token, DateTime ExpiresAt);

/// <summary>
/// Echange le mot de passe unique contre un jeton de session. Voir <see cref="AuthOptions"/>
/// pour le raisonnement complet.
/// </summary>
// PAS d [AllowAnonymous] au niveau du CONTROLEUR : en ASP.NET Core il l emporte sur tout
// [Authorize] d action et ne peut pas etre annule. Pose ici, il rendrait /stream-ticket public
// — n importe qui pourrait obtenir un billet et ouvrir le WebSocket vocal, c est-a-dire parler
// a l assistant ET lui faire executer des actions. L exception est portee par la SEULE action
// qui en a besoin.
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthOptions _options;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IOptions<AuthOptions> options, ILogger<AuthController> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    [AllowAnonymous]   // seule porte ouverte : c est elle qui delivre la session
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogError("[Auth] Auth:Password / Auth:JwtSecret absents ou trop faibles — connexion REFUSEE");
            return StatusCode(503, ApiResponse<LoginResponse>.ErrorResponse(
                "Authentification non configuree sur le serveur", 503));
        }

        // Comparaison a temps CONSTANT : un `==` classique s'arrete au premier caractere
        // different, et cette difference de duree suffit a deviner le mot de passe caractere
        // par caractere. Le cout est nul, l'omission serait une vraie faille.
        var provided = Encoding.UTF8.GetBytes(request.Password ?? string.Empty);
        var expected = Encoding.UTF8.GetBytes(_options.Password);

        if (provided.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            _logger.LogWarning("[Auth] Mot de passe invalide depuis {Ip}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(ApiResponse<LoginResponse>.ErrorResponse("Mot de passe invalide", 401));
        }

        var expires = DateTime.UtcNow.AddDays(_options.TokenLifetimeDays);

        _logger.LogInformation("[Auth] Session ouverte jusqu'au {Expires:yyyy-MM-dd}", expires);

        return Ok(ApiResponse<LoginResponse>.SuccessResponse(
            new LoginResponse(Emettre(OrionAuth.Audience, expires), expires)));
    }

    /// <summary>
    /// Billet de flux — jeton de 60 secondes, seul autorise a voyager dans une URL.
    ///
    /// POURQUOI. SSE et WebSocket de navigateur ne peuvent porter aucun en-tete : leur jeton
    /// doit passer par l URL. Or une URL finit dans les journaux du serveur, ceux du CDN,
    /// l historique et l en-tete Referer. Y faire transiter un jeton valable 30 jours revient a
    /// le publier — constate le 2026-08-26, en clair dans access.log.
    ///
    /// Le billet a une AUDIENCE distincte, pas seulement une duree courte : il ne peut donc pas
    /// servir d en-tete Authorization ailleurs, et le jeton de session ne peut pas le remplacer
    /// dans l URL. Sans cette separation, raccourcir la duree ne fermerait rien.
    ///
    /// 60 s suffisent : le billet ne sert qu a OUVRIR la connexion. Une fois etablie, elle vit
    /// aussi longtemps qu elle veut — l expiration du billet ne la coupe pas.
    /// </summary>
    [Authorize(Policy = OrionAuth.OwnerPolicy)]
    [HttpPost("stream-ticket")]
    public IActionResult DemanderBilletDeFlux()
    {
        if (!_options.IsConfigured)
        {
            return StatusCode(503, ApiResponse<LoginResponse>.ErrorResponse(
                "Authentification non configuree sur le serveur", 503));
        }

        var expires = DateTime.UtcNow.AddSeconds(OrionAuth.StreamTicketLifetimeSeconds);

        return Ok(ApiResponse<LoginResponse>.SuccessResponse(
            new LoginResponse(Emettre(OrionAuth.StreamAudience, expires), expires)));
    }

    /// <summary>
    /// Fabrique UNIQUE des jetons. Session et billet ne different que par audience et duree :
    /// les emettre a deux endroits garantirait qu ils finissent par diverger sur le reste
    /// (role, issuer, algorithme) sans que rien ne le signale.
    /// </summary>
    private string Emettre(string audience, DateTime expires)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret));

        var token = new JwtSecurityToken(
            issuer: OrionAuth.Issuer,
            audience: audience,
            // Le ROLE, pas seulement le nom : c est lui que verifient la politique par defaut
            // et le garde des WebSocket. Un jeton sans role ne satisferait plus rien.
            claims: new[]
            {
                new Claim(ClaimTypes.Name, OrionAuth.OwnerRole),
                new Claim(ClaimTypes.Role, OrionAuth.OwnerRole)
            },
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
