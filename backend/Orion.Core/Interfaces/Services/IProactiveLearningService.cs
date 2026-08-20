using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// La boucle d'apprentissage de la proactivité.
///
/// La table `behavior_patterns` existait depuis le premier jour — entité, repository, DbSet,
/// mapping — avec **zéro écriture et zéro lecture**. Sa colonne `orion_response` était prévue
/// « pour apprendre », d'après le commentaire du schéma. Rien n'avait été construit.
///
/// Ce qu'elle permet est pourtant la seule chose qui rende une proactivité supportable sur la
/// durée : **arrêter de dire ce que l'utilisateur ignore**. Un assistant qui répète dix fois un
/// signal sans effet finit désactivé.
/// </summary>
public interface IProactiveLearningService
{
    /// <summary>Consigne un message réellement prononcé. C'est la trace, pas l'intention.</summary>
    Task<ApiResponse<bool>> EnregistrerSignalementAsync(
        string pattern, string contexte, string message, CancellationToken ct = default);

    /// <summary>
    /// Consigne un retour de l'utilisateur sur un type de signal.
    /// <paramref name="utile"/> false = « ne me dis plus ça ».
    /// </summary>
    Task<ApiResponse<bool>> EnregistrerRetourAsync(
        string pattern, bool utile, string? motif, CancellationToken ct = default);

    /// <summary>
    /// Pénalité apprise par pattern, de 0 à 100, à soustraire du score d'urgence.
    /// Un pattern rejeté plusieurs fois finit sous le seuil d'interruption, puis se tait.
    /// </summary>
    Task<ApiResponse<Dictionary<string, int>>> ObtenirPenalitesAsync(CancellationToken ct = default);
}
