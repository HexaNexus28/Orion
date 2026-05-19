namespace Orion.Daemon.Core.Entities;

public class DaemonCommand
{
    public string Action { get; set; } = "";
    public object Payload { get; set; } = new();
    public string CorrelationId { get; set; } = "";
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // Backend serializes as "RequestId" — alias for cross-boundary compatibility
    public string RequestId { get => CorrelationId; set => CorrelationId = value; }
}
