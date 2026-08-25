namespace Orion.Core.DTOs.Responses;

/// <summary>
/// Ce que la consolidation a réellement fait. Rendu à l'utilisateur : une mémoire qui se
/// réécrit sans rendre de comptes est une mémoire à laquelle on ne peut pas faire confiance.
/// </summary>
public class ConsolidationReport
{
    public int EpisodesExamines { get; set; }
    public int SouvenirsEcrits { get; set; }

    /// <summary>Épisodes bruts retirés après distillation — ils vivent toujours dans `messages`.</summary>
    public int EpisodesConsommes { get; set; }
    public int EtatsPerimesSupprimes { get; set; }

    /// <summary>Les faits distillés, préfixés de leur emplacement. Pour affichage.</summary>
    public List<string> Distilles { get; set; } = new();

    public string Resume { get; set; } = string.Empty;
}
