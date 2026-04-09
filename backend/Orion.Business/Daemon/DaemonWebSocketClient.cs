using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;

namespace Orion.Business.Daemon;

public class DaemonWebSocketClient : IDaemonClient
{
    private readonly ILogger<DaemonWebSocketClient> _logger;
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DaemonActionResponse>> _pendingRequests = new();

    public bool IsConnected => _connections.Any(c => c.Value.State == WebSocketState.Open);
    public string MachineName => _connections.Keys.FirstOrDefault() ?? "unknown";

    public DaemonWebSocketClient(ILogger<DaemonWebSocketClient> logger)
    {
        _logger = logger;
    }

    public void RegisterConnection(string machineName, WebSocket webSocket)
    {
        _connections[machineName] = webSocket;
        _logger.LogInformation("Daemon connected from {MachineName}", machineName);

        _ = ReceiveLoopAsync(machineName, webSocket);
    }

    public async Task<ApiResponse<DaemonActionResponse>> SendActionAsync(DaemonActionRequest action, CancellationToken ct)
    {
        var connection = _connections.FirstOrDefault(c => c.Value.State == WebSocketState.Open);
        if (connection.Value == null)
        {
            return ApiResponse<DaemonActionResponse>.ErrorResponse("Daemon not connected");
        }

        var tcs = new TaskCompletionSource<DaemonActionResponse>();
        _pendingRequests[action.RequestId] = tcs;

        try
        {
            var message = JsonSerializer.Serialize(action);
            var bytes = Encoding.UTF8.GetBytes(message);
            await connection.Value.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await tcs.Task.WaitAsync(cts.Token);

            return response.Success
                ? ApiResponse<DaemonActionResponse>.SuccessResponse(response)
                : ApiResponse<DaemonActionResponse>.ErrorResponse(response.Error ?? "Unknown error");
        }
        catch (TimeoutException)
        {
            return ApiResponse<DaemonActionResponse>.ErrorResponse("Daemon request timeout");
        }
        catch (Exception ex)
        {
            return ApiResponse<DaemonActionResponse>.ErrorResponse($"Failed to send action: {ex.Message}");
        }
        finally
        {
            _pendingRequests.TryRemove(action.RequestId, out _);
        }
    }

    private async Task ReceiveLoopAsync(string machineName, WebSocket webSocket)
    {
        var buffer = new byte[4096];

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var response = JsonSerializer.Deserialize<DaemonActionResponse>(message);

                if (response?.RequestId != null && _pendingRequests.TryRemove(response.RequestId, out var tcs))
                {
                    tcs.SetResult(response);
                }
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "Daemon connection error from {MachineName}", machineName);
        }
        finally
        {
            _connections.TryRemove(machineName, out _);
            _logger.LogInformation("Daemon disconnected from {MachineName}", machineName);
        }
    }
}
