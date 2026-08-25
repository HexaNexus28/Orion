namespace Orion.Core.DTOs;

public class HealthCheckDto
{
    public string Status { get; set; } = "healthy";
    public string LlmProvider { get; set; } = "None";

    /// <summary>Modèle réellement actif — pour qu'un repli silencieux ne soit plus invisible.</summary>
    public string LlmModel { get; set; } = "aucun";

    public DateTime Timestamp { get; set; }
}
