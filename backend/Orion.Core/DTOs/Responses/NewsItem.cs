using Orion.Core.Configuration;

namespace Orion.Core.DTOs.Responses;

/// <summary>Un article collecte. Aucun champ n'est produit par le modele : tout vient du flux.</summary>
public class NewsItem
{
    public string Title { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public NewsCircle Circle { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public DateTimeOffset PublishedAt { get; set; }

    /// <summary>Resume fourni PAR LE FLUX, tronque. Vide si la source n'en donne pas.</summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// Resultat d'une collecte. <see cref="FailedFeeds"/> n'est pas decoratif : un flux mort doit
/// se voir, sinon le briefing retrecit en silence et personne ne sait pourquoi.
/// </summary>
public class NewsHarvest
{
    public List<NewsItem> Items { get; set; } = new();
    public List<string> FailedFeeds { get; set; } = new();
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.UtcNow;
}
