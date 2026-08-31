namespace Orion.Core.DTOs.Responses;

public class BriefingDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object>? Stats { get; set; }

    /// <summary>
    /// Les articles reellement collectes. Toute la garantie anti-invention repose sur le fait
    /// que chaque sujet du briefing vient d'une URL : ne pas les renvoyer rendrait cette
    /// garantie invisible, donc invérifiable.
    /// </summary>
    public List<BriefingSource> Sources { get; set; } = new();
}

public class BriefingSource
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;

    /// <summary>« local », « africa » ou « world ».</summary>
    public string Circle { get; set; } = string.Empty;
}
