using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// Apprend quels signaux l'utilisateur ignore, pour cesser de les dire.
///
/// Convention de stockage — choisie pour ne PAS migrer le schéma de production :
///   `pattern_type = "high_ram"`          → un signalement réellement prononcé
///   `pattern_type = "feedback:high_ram"` → un retour de l'utilisateur sur ce signal
/// Les deux vivent dans `behavior_patterns` et se distinguent par le préfixe, qui reste
/// requêtable via `GetByPatternTypeAsync`.
/// </summary>
public class ProactiveLearningService : IProactiveLearningService
{
    private const string PrefixeRetour = "feedback:";

    /// <summary>Marqueurs stockés dans `orion_response` : le verdict tient en un mot.</summary>
    private const string Rejete = "REJETE";
    private const string Utile = "UTILE";

    /// <summary>
    /// Pénalité par rejet. Trois rejets suffisent à faire passer un signal moyen sous le seuil
    /// d'interruption : l'utilisateur n'a pas à le répéter dix fois.
    /// </summary>
    private const int PenaliteParRejet = 20;

    /// <summary>Un retour positif compense un rejet — un avis peut changer.</summary>
    private const int BonusParUtile = 20;

    private const int PenaliteMax = 100;

    /// <summary>
    /// Au-delà, un retour est trop vieux pour peser : les habitudes changent, et un « non »
    /// d'il y a six mois ne doit pas condamner un signal pour toujours.
    /// </summary>
    private const int JoursDeMemoire = 90;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProactiveLearningService> _logger;

    public ProactiveLearningService(IUnitOfWork unitOfWork, ILogger<ProactiveLearningService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ApiResponse<bool>> EnregistrerSignalementAsync(
        string pattern, string contexte, string message, CancellationToken ct = default)
    {
        try
        {
            await _unitOfWork.BehaviorPatterns.AddAsync(new BehaviorPattern
            {
                Id = Guid.NewGuid(),
                PatternType = pattern,
                Context = contexte,
                OrionResponse = message,
                ObservedAt = DateTime.UtcNow
            }, ct);

            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (Exception ex)
        {
            // Ne jamais faire échouer une notification parce que sa trace n'a pas pu s'écrire.
            _logger.LogWarning(ex, "[Apprentissage] Signalement non consigne pour {Pattern}", pattern);
            return ApiResponse<bool>.SuccessResponse(false);
        }
    }

    public async Task<ApiResponse<bool>> EnregistrerRetourAsync(
        string pattern, bool utile, string? motif, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return ApiResponse<bool>.ErrorResponse("Le type de signal est requis", 400);

        try
        {
            await _unitOfWork.BehaviorPatterns.AddAsync(new BehaviorPattern
            {
                Id = Guid.NewGuid(),
                PatternType = PrefixeRetour + pattern.Trim(),
                Context = motif,
                OrionResponse = utile ? Utile : Rejete,
                ObservedAt = DateTime.UtcNow
            }, ct);

            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation("[Apprentissage] Retour sur {Pattern} : {Verdict}",
                pattern, utile ? Utile : Rejete);

            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Apprentissage] Retour non consigne pour {Pattern}", pattern);
            return ApiResponse<bool>.ErrorResponse("Retour non enregistre", 500);
        }
    }

    public async Task<ApiResponse<Dictionary<string, int>>> ObtenirPenalitesAsync(CancellationToken ct = default)
    {
        try
        {
            var depuis = DateTime.UtcNow.AddDays(-JoursDeMemoire);
            var observations = await _unitOfWork.BehaviorPatterns.GetSinceAsync(depuis, ct);

            var penalites = observations
                .Where(o => o.PatternType.StartsWith(PrefixeRetour, StringComparison.OrdinalIgnoreCase))
                .GroupBy(o => o.PatternType[PrefixeRetour.Length..], StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Pattern = g.Key,
                    Penalite = Math.Clamp(
                        g.Count(o => Rejete.Equals(o.OrionResponse, StringComparison.OrdinalIgnoreCase)) * PenaliteParRejet
                        - g.Count(o => Utile.Equals(o.OrionResponse, StringComparison.OrdinalIgnoreCase)) * BonusParUtile,
                        0, PenaliteMax)
                })
                .Where(x => x.Penalite > 0)
                .ToDictionary(x => x.Pattern, x => x.Penalite, StringComparer.OrdinalIgnoreCase);

            return ApiResponse<Dictionary<string, int>>.SuccessResponse(penalites);
        }
        catch (Exception ex)
        {
            // Une pénalité indisponible ne doit pas rendre ORION muet : sans apprentissage,
            // il se comporte comme avant.
            _logger.LogWarning(ex, "[Apprentissage] Penalites indisponibles");
            return ApiResponse<Dictionary<string, int>>.SuccessResponse(new Dictionary<string, int>());
        }
    }
}
