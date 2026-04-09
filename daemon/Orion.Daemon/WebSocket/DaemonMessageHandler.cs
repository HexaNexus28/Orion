using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.WebSocket;

public class DaemonMessageHandler
{
    private readonly IActionRegistry _actionRegistry;
    private readonly ILogger _logger;

    public DaemonMessageHandler(IActionRegistry actionRegistry, ILogger logger)
    {
        _actionRegistry = actionRegistry;
        _logger = logger;
    }

    public async Task<DaemonResponse> ProcessMessageAsync(string message)
    {
        try
        {
            var command = JsonSerializer.Deserialize<DaemonCommand>(message);
            if (command == null)
            {
                return DaemonResponse.ErrorResponse(Guid.NewGuid().ToString(), "Failed to parse command");
            }

            _logger.LogInformation("[DAEMON] Executing action: {Action}", command.Action);

            var action = _actionRegistry.Get(command.Action);
            if (action == null)
            {
                return DaemonResponse.ErrorResponse(command.CorrelationId, $"Unknown action: {command.Action}");
            }

            return await action.ExecuteAsync(
                command.Payload is JsonElement json ? json : JsonSerializer.SerializeToElement(command.Payload),
                command.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DAEMON] Failed to process message");
            return DaemonResponse.ErrorResponse(Guid.NewGuid().ToString(), ex.Message);
        }
    }
}
