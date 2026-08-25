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
        // L'identifiant de corrélation est extrait AVANT toute opération risquée.
        //
        // Sinon une action qui lève renvoie une erreur portant un identifiant aléatoire : le
        // backend attend toujours l'identifiant d'origine, ne le voit jamais arriver, et finit
        // en timeout opaque (« A task was canceled »). Chaque bug d'action devenait ainsi
        // indiagnosticable — c'est ce qui masquait le désaccord de contrat sur `open_app`.
        var correlationId = TryReadCorrelationId(message);

        try
        {
            var command = JsonSerializer.Deserialize<DaemonCommand>(message);
            if (command == null)
            {
                return DaemonResponse.ErrorResponse(correlationId, "Failed to parse command");
            }

            correlationId = string.IsNullOrEmpty(command.CorrelationId) ? correlationId : command.CorrelationId;

            _logger.LogInformation("[DAEMON] Executing action: {Action}", command.Action);

            var action = _actionRegistry.Get(command.Action);
            if (action == null)
            {
                return DaemonResponse.ErrorResponse(correlationId, $"Unknown action: {command.Action}");
            }

            return await action.ExecuteAsync(
                command.Payload is JsonElement json ? json : JsonSerializer.SerializeToElement(command.Payload),
                correlationId);
        }
        catch (KeyNotFoundException ex)
        {
            // Champ absent du payload = désaccord de contrat entre le tool backend et l'action
            // daemon. On le nomme explicitement plutôt que de laisser un message générique.
            _logger.LogError(ex, "[DAEMON] Champ manquant dans le payload — contrat tool/action desaccorde");
            return DaemonResponse.ErrorResponse(correlationId,
                $"Payload incomplet (contrat tool/daemon desaccorde) : {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DAEMON] Failed to process message");
            return DaemonResponse.ErrorResponse(correlationId, ex.Message);
        }
    }

    /// <summary>
    /// Lit le correlationId directement dans le JSON brut, sans dépendre de la désérialisation
    /// complète — qui peut elle-même échouer.
    /// </summary>
    private static string TryReadCorrelationId(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            foreach (var name in new[] { "correlationId", "CorrelationId", "requestId", "RequestId" })
            {
                if (document.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    var id = value.GetString();
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
        }
        catch (JsonException)
        {
            // Message illisible : on ne peut rien corréler, l'appelant expirera de toute façon.
        }

        return Guid.NewGuid().ToString("N");
    }
}
