namespace Orion.Core.DTOs.Responses;

public class DaemonActionResponse
{
    public string RequestId { get; set; } = "";
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }
    public long Timestamp { get; set; }

    // Daemon serializes as "CorrelationId" — alias for cross-boundary compatibility
    public string CorrelationId { get => RequestId; set => RequestId = value; }
}
