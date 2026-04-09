using System.Net.WebSockets;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Daemon;

public interface IDaemonClient
{
    Task<ApiResponse<DaemonActionResponse>> SendActionAsync(DaemonActionRequest action, CancellationToken ct = default);
    void RegisterConnection(string machineName, WebSocket webSocket);
    bool IsConnected { get; }
    string MachineName { get; }
}
