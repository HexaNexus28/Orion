namespace Orion.Core.DTOs;

/// <summary>
/// DTO pour les résultats de recherche sémantique
/// </summary>
public class MemoryVectorDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public float Similarity { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAt { get; set; }
}
