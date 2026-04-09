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
        var buffer = new byte[4096];
        var handler = new DaemonMessageHandler(_actionRegistry, _logger);

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var response = await handler.ProcessMessageAsync(message);

                await SendResponseAsync(response, ct);
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
}
