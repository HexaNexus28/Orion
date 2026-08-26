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
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly AuthOptions _options;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IOptions<AuthOptions> options, ILogger<AuthController> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

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
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret));

        var token = new JwtSecurityToken(
            issuer: "orion",
            audience: "orion",
            // Le ROLE, pas seulement le nom : c.est lui que verifient la politique par defaut et
            // le garde des WebSocket. Un jeton sans role ne satisferait plus rien.
            claims: new[]
            {
                new Claim(ClaimTypes.Name, OrionAuth.OwnerRole),
                new Claim(ClaimTypes.Role, OrionAuth.OwnerRole)
            },
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        _logger.LogInformation("[Auth] Session ouverte jusqu'au {Expires:yyyy-MM-dd}", expires);

        return Ok(ApiResponse<LoginResponse>.SuccessResponse(
            new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires)));
    }
}
