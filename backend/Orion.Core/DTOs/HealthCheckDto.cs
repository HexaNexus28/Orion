namespace Orion.Core.DTOs;

public class HealthCheckDto
{
    public string Status { get; set; } = "healthy";
    public string LlmProvider { get; set; } = "None";
    public DateTime Timestamp { get; set; }
}
