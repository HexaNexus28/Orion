namespace Orion.Core.DTOs.Requests;

/// <summary>
/// Request body for memory search (RAG)
/// </summary>
public class MemorySearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int Limit { get; set; } = 5;
}
