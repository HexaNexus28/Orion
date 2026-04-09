namespace Orion.Core.Configuration;

public class InternetOptions
{
    public const string SectionName = "Internet";
    
    public string SearchApiProvider { get; set; } = "brave"; // brave, serpapi
    public string BraveApiKey { get; set; } = string.Empty;
    public string SerpApiKey { get; set; } = string.Empty;
    public int MaxResults { get; set; } = 5;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public string[] BlockedDomains { get; set; } = Array.Empty<string>();
}
