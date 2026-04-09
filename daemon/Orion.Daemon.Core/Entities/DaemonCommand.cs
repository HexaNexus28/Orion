namespace Orion.Daemon.Core.Entities;

public class DaemonCommand
{
    public string Action { get; set; } = "";
    public object Payload { get; set; } = new();
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
