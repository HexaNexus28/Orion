namespace Orion.Core.DTOs.Requests;

public class DaemonActionRequest
{
    public string Action { get; set; } = "";
    public object Payload { get; set; } = new();
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
}
