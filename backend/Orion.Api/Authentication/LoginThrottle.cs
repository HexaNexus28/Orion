using Microsoft.Extensions.Options;
using Orion.Core.Configuration;

namespace Orion.Api.Authentication;

/// <summary>
/// Plafonne la devinette du mot de passe sur `/api/auth/login`, seule porte anonyme de l'API.
///
/// Seuls les ECHECS sont comptes, et le controleur verifie le mot de passe AVANT d'appeler le
/// frein : un mot de passe correct passe toujours, meme quota epuise. Sans cette asymetrie, un
/// attaquant saturant la fenetre enfermerait le proprietaire dehors.
///
/// Pas de partition par IP : sans `UseForwardedHeaders`, `RemoteIpAddress` vaut le proxy pour
/// toutes les requetes. Le debit brut releve de `limit_req` cote Nginx. Voir docs/security.md.
/// </summary>
public sealed class LoginThrottle
{
    private readonly AuthOptions _options;
    private readonly ILogger<LoginThrottle> _logger;

    private readonly Queue<DateTimeOffset> _failures = new();
    private readonly object _gate = new();

    public LoginThrottle(IOptions<AuthOptions> options, ILogger<LoginThrottle> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Quota epuise ? <paramref name="retryAfter"/> alimente l'en-tete `Retry-After`.
    /// </summary>
    public bool IsBlocked(out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            Purge();

            if (_failures.Count < _options.LoginFailuresPerWindow)
            {
                retryAfter = TimeSpan.Zero;
                return false;
            }

            var window = TimeSpan.FromMinutes(_options.LoginWindowMinutes);
            retryAfter = _failures.Peek() + window - DateTimeOffset.UtcNow;

            // Une expiration peut tomber entre la purge et ce calcul : un `Retry-After` negatif
            // dirait au client de reessayer dans le passe.
            if (retryAfter <= TimeSpan.Zero) { retryAfter = TimeSpan.FromSeconds(1); }

            return true;
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            Purge();
            _failures.Enqueue(DateTimeOffset.UtcNow);

            if (_failures.Count >= _options.LoginFailuresPerWindow)
            {
                _logger.LogWarning(
                    "[Auth] Quota d'echecs atteint : {Failures} en {Minutes} min — mots de passe "
                    + "errones refuses en 429 jusqu'a expiration de la fenetre",
                    _failures.Count, _options.LoginWindowMinutes);
            }
        }
    }

    /// <summary>Appele sur connexion reussie : des fautes de frappe ne doivent pas entamer le quota.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _failures.Clear();
        }
    }

    private void Purge()
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(_options.LoginWindowMinutes);
        while (_failures.Count > 0 && _failures.Peek() < cutoff)
        {
            _failures.Dequeue();
        }
    }
}
