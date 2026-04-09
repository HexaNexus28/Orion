namespace Orion.Daemon.Core.Configuration;

public class DaemonOptions
{
    public string RenderWsUrl { get; set; } = "wss://orion-api.onrender.com/daemon";
    public string Token { get; set; } = "";
    public string MachineName { get; set; } = Environment.MachineName;
    public int ReconnectDelayMs { get; set; } = 5000;
    public int MaxReconnectDelayMs { get; set; } = 60000;
    public double ReconnectMultiplier { get; set; } = 2.0;
}
