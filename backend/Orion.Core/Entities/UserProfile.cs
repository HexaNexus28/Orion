namespace Orion.Core.Entities;

public class UserProfile
{
    public string Key { get; set; } = string.Empty; // PK
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
