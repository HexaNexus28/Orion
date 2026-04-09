namespace Orion.Core.DTOs.Responses;

public class BriefingDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object>? Stats { get; set; }
}
