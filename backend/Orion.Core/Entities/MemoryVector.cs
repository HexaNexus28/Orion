namespace Orion.Core.Entities;

public class MemoryVector
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>(); // vector(768) for pgvector
    public string? Source { get; set; } // 'conversation' | 'briefing' | 'manual'
    public float Importance { get; set; } = 1.0f; // 0.0 to 1.0
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastAccessed { get; set; }
}
