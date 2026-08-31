using Microsoft.Extensions.Options;
using Orion.Core.Configuration;

namespace Orion.Api.Authentication;

/// <summary>
/// Frein sur la devinette du mot de passe — dernier point ouvert de l'audit d'authentification.
///
/// LE PROBLEME. `/api/auth/login` est la seule porte anonyme de l'API, et elle garde un mot de
/// passe UNIQUE : pas d'identifiant a deviner en plus, pas de second facteur. Sans frein, il est
/// attaquable en force brute aussi vite que le reseau le permet. La comparaison a temps constant
/// deja en place ne protege de rien ici : elle empeche de deviner le mot de passe CARACTERE par
/// caractere, pas de l'essayer en entier un million de fois.
///
/// POURQUOI PAS `AddRateLimiter` PARTITIONNE PAR IP — le reflexe standard, et il serait FAUX ici.
/// ORION vit derriere Cloudflare puis derriere la facade Nginx, et `Program.cs` n'installe aucun
/// `UseForwardedHeaders`. `HttpContext.Connection.RemoteIpAddress` vaut donc l'adresse du proxy
/// pour TOUTES les requetes. Partitionner la-dessus produirait un limiteur global deguise en
/// limiteur par client : precisement le motif que l'audit du 2026-08-27 condamne — une option
/// qui se fait passer pour une defense.
///
/// POURQUOI PAS UN LIMITEUR GLOBAL NON PLUS. Il a le defaut symetrique : un attaquant qui sature
/// la fenetre enferme le PROPRIETAIRE dehors. L'attaque devient un deni de service sur le compte
/// qu'elle visait.
///
/// CE QUI EST FAIT A LA PLACE : seuls les ECHECS sont comptes, et le mot de passe est verifie
/// AVANT le frein. Un mot de passe correct ne consomme aucun credit et passe toujours, meme en
/// pleine attaque. Le devinage, lui, est plafonne a N essais par fenetre.
///
/// LE FLOOD DE REQUETES n'est deliberement PAS traite ici : un essai coute une comparaison de
/// quelques octets. Le debit brut se plafonne a l'etage qui sait le faire — `limit_req` cote
/// Nginx, deja surveille par la jail fail2ban `nginx-limit-req`.
///
/// EN MEMOIRE, et c'est suffisant : un seul conteneur sert l'API. Un redemarrage remet le
/// compteur a zero — sans consequence, puisque redemarrer n'est pas a la portee de l'attaquant.
/// </summary>
public sealed class LoginThrottle
{
    private readonly AuthOptions _options;
    private readonly ILogger<LoginThrottle> _logger;

    // Les instants des echecs encore dans la fenetre. Une file suffit : ils arrivent dans
    // l'ordre, donc les plus anciens sortent toujours par la tete.
    private readonly Queue<DateTimeOffset> _echecs = new();
    private readonly object _verrou = new();

    public LoginThrottle(IOptions<AuthOptions> options, ILogger<LoginThrottle> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Le quota d'echecs est-il epuise ? <paramref name="reessayerDans"/> porte le temps restant
    /// avant que le plus ancien echec ne sorte de la fenetre — c'est ce qui alimente `Retry-After`.
    /// </summary>
    public bool EstBloque(out TimeSpan reessayerDans)
    {
        lock (_verrou)
        {
            Purger();

            if (_echecs.Count < _options.LoginFailuresPerWindow)
            {
                reessayerDans = TimeSpan.Zero;
                return false;
            }

            var fenetre = TimeSpan.FromMinutes(_options.LoginWindowMinutes);
            reessayerDans = _echecs.Peek() + fenetre - DateTimeOffset.UtcNow;

            // Une valeur negative ou nulle n'a aucun sens dans un `Retry-After` : elle
            // signifierait « reessaie dans le passe ». La purge vient de tourner, mais une
            // expiration peut tomber entre les deux instructions.
            if (reessayerDans <= TimeSpan.Zero) { reessayerDans = TimeSpan.FromSeconds(1); }

            return true;
        }
    }

    /// <summary>Un essai infructueux de plus. Seuls les echecs comptent.</summary>
    public void EnregistrerEchec()
    {
        lock (_verrou)
        {
            Purger();
            _echecs.Enqueue(DateTimeOffset.UtcNow);

            if (_echecs.Count >= _options.LoginFailuresPerWindow)
            {
                _logger.LogWarning(
                    "[Auth] Quota d'echecs atteint : {Echecs} en {Minutes} min — les mots de passe "
                    + "errones sont desormais refuses en 429 jusqu'a expiration de la fenetre",
                    _echecs.Count, _options.LoginWindowMinutes);
            }
        }
    }

    /// <summary>
    /// Connexion reussie : l'ardoise est effacee. Sans ca, quelques fautes de frappe suivies
    /// d'une connexion legitime laisseraient un quota entame pour la suite.
    /// </summary>
    public void Reinitialiser()
    {
        lock (_verrou)
        {
            _echecs.Clear();
        }
    }

    /// <summary>Fenetre GLISSANTE : on retire par la tete tout ce qui est sorti de la fenetre.</summary>
    private void Purger()
    {
        var limite = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(_options.LoginWindowMinutes);
        while (_echecs.Count > 0 && _echecs.Peek() < limite)
        {
            _echecs.Dequeue();
        }
    }
}
