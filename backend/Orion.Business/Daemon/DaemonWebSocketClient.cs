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
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]?>> _pendingBinaryRequests = new();

    public bool IsConnected => _connections.Any(c => c.Value.State == WebSocketState.Open);
    public string MachineName => _connections.Keys.FirstOrDefault() ?? "unknown";

    public DaemonWebSocketClient(ILogger<DaemonWebSocketClient> logger)
    {
        _logger = logger;
    }

    public Task RegisterConnectionAsync(string machineName, WebSocket webSocket, CancellationToken ct = default)
    {
        _connections[machineName] = webSocket;
        _logger.LogInformation("Daemon connected from {MachineName}", machineName);

        // Return the receive loop so the caller (middleware) can await it
        // without creating a second concurrent ReceiveAsync on the same socket
        return ReceiveLoopAsync(machineName, webSocket, ct);
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

    /// <summary>
    /// Request raw WAV bytes from daemon (binary WS protocol).
    /// Used by SynthesizeAsync to avoid base64 encode/decode overhead.
    /// </summary>
    public async Task<ApiResponse<byte[]?>> SendBinaryActionAsync(DaemonActionRequest action, CancellationToken ct)
    {
        var connection = _connections.FirstOrDefault(c => c.Value.State == WebSocketState.Open);
        if (connection.Value == null)
            return ApiResponse<byte[]?>.ErrorResponse("Daemon not connected");

        var tcs = new TaskCompletionSource<byte[]?>();
        _pendingBinaryRequests[action.RequestId] = tcs;

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
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var wavBytes = await tcs.Task.WaitAsync(cts.Token);
            return ApiResponse<byte[]?>.SuccessResponse(wavBytes);
        }
        catch (Exception ex)
        {
            return ApiResponse<byte[]?>.ErrorResponse($"Binary action failed: {ex.Message}");
        }
        finally
        {
            _pendingBinaryRequests.TryRemove(action.RequestId, out _);
        }
    }

    private async Task ReceiveLoopAsync(string machineName, WebSocket webSocket, CancellationToken ct)
    {
        var buffer = new byte[65536];

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                // Accumulate multi-frame messages
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Binary protocol: [36-byte requestId UTF-8] + [raw WAV bytes]
                    var data = ms.ToArray();
                    if (data.Length > 36)
                    {
                        var requestId = Encoding.UTF8.GetString(data, 0, 36).Trim();
                        var wavBytes = new byte[data.Length - 36];
                        Buffer.BlockCopy(data, 36, wavBytes, 0, wavBytes.Length);

                        if (_pendingBinaryRequests.TryRemove(requestId, out var binaryTcs))
                        {
                            binaryTcs.SetResult(wavBytes);
                            _logger.LogDebug("Received {Kb}KB binary WAV from daemon", wavBytes.Length / 1024);
                        }
                        else
                        {
                            _logger.LogWarning("Received binary frame for unknown requestId: {Id}", requestId);
                        }
                    }
                }
                else
                {
                    // Text JSON response (non-binary actions)
                    var message = Encoding.UTF8.GetString(ms.ToArray());
                    var response = JsonSerializer.Deserialize<DaemonActionResponse>(message);

                    if (response?.RequestId != null && _pendingRequests.TryRemove(response.RequestId, out var tcs))
                    {
                        tcs.SetResult(response);
                    }
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
