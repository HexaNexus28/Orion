namespace Orion.Core.Configuration;

public class DaemonOptions
{
    public const string SectionName = "Daemon";
    
    public string WsUrl { get; set; } = "ws://localhost:5001/ws";
    public string WsToken { get; set; } = string.Empty;
}
