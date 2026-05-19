using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.WebSocket;

public class DaemonWebSocketManager
{
    private readonly DaemonOptions _options;
    private readonly IActionRegistry _actionRegistry;
    private readonly ILogger _logger;
    private ClientWebSocket? _webSocket;
    private int _currentReconnectDelay;

    public DaemonWebSocketManager(
        DaemonOptions options,
        IActionRegistry actionRegistry,
        ILogger logger)
    {
        _options = options;
        _actionRegistry = actionRegistry;
        _logger = logger;
        _currentReconnectDelay = options.ReconnectDelayMs;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _webSocket = new ClientWebSocket();
                _webSocket.Options.SetRequestHeader("X-Daemon-Token", _options.Token);
                _webSocket.Options.SetRequestHeader("X-Machine-Name", _options.MachineName);

                _logger.LogInformation("[DAEMON] Connecting to {Url}...", _options.RenderWsUrl);
                await _webSocket.ConnectAsync(new Uri(_options.RenderWsUrl), ct);

                _logger.LogInformation("[DAEMON] Connected to backend");
                _currentReconnectDelay = _options.ReconnectDelayMs; // Reset on success

                await ReceiveLoopAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError("[DAEMON] Connection error: {Message}", ex.Message);
                _logger.LogInformation("[DAEMON] Reconnecting in {Delay}ms...", _currentReconnectDelay);

                await Task.Delay(_currentReconnectDelay, ct);
                _currentReconnectDelay = Math.Min(
                    (int)(_currentReconnectDelay * _options.ReconnectMultiplier),
                    _options.MaxReconnectDelayMs);
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536]; // 64KB — matches backend buffer
        var handler = new DaemonMessageHandler(_actionRegistry, _logger);

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // Accumulate multi-frame messages
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var message = Encoding.UTF8.GetString(ms.ToArray());
                var response = await handler.ProcessMessageAsync(message);

                // Send binary WAV directly if available, otherwise JSON
                if (response.Success && response.Data is BinaryPayload bin)
                {
                    await SendBinaryResponseAsync(response.RequestId, bin.Bytes, ct);
                }
                else
                {
                    await SendResponseAsync(response, ct);
                }
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning("[DAEMON] WebSocket disconnected: {Message}", ex.Message);
        }
    }

    private async Task SendResponseAsync(DaemonResponse response, CancellationToken ct)
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            var json = JsonSerializer.Serialize(response);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                ct);
        }
    }

    /// <summary>
    /// Send binary response: [36-byte requestId UTF-8] + [raw WAV bytes]
    /// Backend detects binary frames and resolves pending requests without base64.
    /// </summary>
    private async Task SendBinaryResponseAsync(string requestId, byte[] wavBytes, CancellationToken ct)
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        // Protocol: first 36 bytes = requestId (GUID string), rest = WAV
        var idBytes = Encoding.UTF8.GetBytes(requestId.PadRight(36)[..36]);
        var frame = new byte[36 + wavBytes.Length];
        Buffer.BlockCopy(idBytes, 0, frame, 0, 36);
        Buffer.BlockCopy(wavBytes, 0, frame, 36, wavBytes.Length);

        await _webSocket.SendAsync(
            new ArraySegment<byte>(frame),
            WebSocketMessageType.Binary,
            true,
            ct);

        _logger.LogDebug("[DAEMON] Sent {Kb}KB binary WAV for {Id}", wavBytes.Length / 1024, requestId);
    }
}
