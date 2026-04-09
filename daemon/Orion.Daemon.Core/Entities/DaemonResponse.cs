namespace Orion.Daemon.Core.Entities;

public class DaemonResponse
{
    public string CorrelationId { get; set; } = "";
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static DaemonResponse SuccessResponse(string correlationId, object? data = null)
    {
        return new DaemonResponse
        {
            CorrelationId = correlationId,
            Success = true,
            Data = data
        };
    }

    public static DaemonResponse ErrorResponse(string correlationId, string error)
    {
        return new DaemonResponse
        {
            CorrelationId = correlationId,
            Success = false,
            Error = error
        };
    }
}
