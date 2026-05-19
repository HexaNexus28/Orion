using System.Net.WebSockets;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Daemon;

public interface IDaemonClient
{
    Task<ApiResponse<DaemonActionResponse>> SendActionAsync(DaemonActionRequest action, CancellationToken ct = default);
    Task<ApiResponse<byte[]?>> SendBinaryActionAsync(DaemonActionRequest action, CancellationToken ct = default);
    Task RegisterConnectionAsync(string machineName, WebSocket webSocket, CancellationToken ct = default);
    bool IsConnected { get; }
    string MachineName { get; }
}
