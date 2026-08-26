using System.Text.Json;
using System.Text.Json.Nodes;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class GetSystemStatusTool : ITool
{
    private readonly IDaemonClient _daemon;

    public GetSystemStatusTool(IDaemonClient daemon)
    {
        _daemon = daemon;
    }

    public string Name => "get_system_status";
    public string Description => "Retourne le statut du système Windows (CPU, RAM, disque, processus actifs)";

    public bool RequiresDaemon => true;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject()
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {

        var request = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "system_status",
            Payload = new { }
        };

        var result = await _daemon.SendActionAsync(request, ct);

        if (!result.Success)
        {
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);
        }

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            JsonSerializer.Serialize(result.Data?.Data),
            Name));
    }

    /// <summary>
    /// Carte « poste ». Les donnees viennent du daemon (cf. GetSystemStatusAction).
    ///
    /// `workingSetMb` est volontairement etiquete « Memoire ORION » et NON « RAM » : c est la
    /// memoire du PROCESSUS daemon, pas celle du systeme. L appeler RAM afficherait 80 Mo sur une
    /// machine de 32 Go — une carte fausse est pire que pas de carte.
    /// </summary>
    public HudCard? BuildCard(ToolResult result)
    {
        if (result.Data is not string json) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;

            string? Lire(string nom) => r.TryGetProperty(nom, out var v) ? v.ToString() : null;

            var minutes = long.TryParse(Lire("uptimeMinutes"), out var m) ? m : 0;
            var duree = minutes >= 60 ? $"{minutes / 60} h {minutes % 60:D2}" : $"{minutes} min";

            var items = new List<HudCardItem>();
            if (Lire("processorCount") is { } coeurs) items.Add(new HudCardItem { Label = "Coeurs", Value = coeurs });
            if (Lire("workingSetMb") is { } mo)      items.Add(new HudCardItem { Label = "Memoire ORION", Value = mo + " Mo" });
            if (Lire("localTime") is { } heure)      items.Add(new HudCardItem { Label = "Heure du poste", Value = heure });

            return new HudCard
            {
                Id = "system.host",
                Kind = HudCardKind.Status,
                Label = Lire("machineName") ?? "Poste",
                Value = duree,
                Unit = "en service",
                State = HudCardState.Ok,
                Items = items.Count > 0 ? items : null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
