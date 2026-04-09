namespace Orion.Core.Entities;

public class BehaviorPattern
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PatternType { get; set; } = string.Empty;
    public DateTime ObservedAt { get; set; } = DateTime.UtcNow;
    public string? Context { get; set; }
    public string? OrionResponse { get; set; }
}
