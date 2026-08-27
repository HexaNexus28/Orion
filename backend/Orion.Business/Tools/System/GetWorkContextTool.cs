using System.Text.Json;
using System.Text.Json.Nodes;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

/// <summary>
/// Ce sur quoi l'utilisateur travaille en ce moment.
///
/// `get_system_status` répond « quelle machine ». Celui-ci répond « quel travail » — et c'est ce
/// qui permet à ORION de proposer le geste suivant au lieu d'afficher un indicateur.
/// </summary>
public class GetWorkContextTool : ITool
{
    private readonly IDaemonClient _daemon;

    public GetWorkContextTool(IDaemonClient daemon) => _daemon = daemon;

    public string Name => "get_work_context";

    public string Description =>
        "Ce sur quoi l'utilisateur travaille maintenant : application au premier plan, fichier et "
        + "projet ouverts. À utiliser avant de proposer une action liée à son travail en cours.";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
    };

    public bool RequiresDaemon => true;

    /// <summary>
    /// Non différable : « sur quoi travaillait-il il y a douze heures » ne vaut rien. Une lecture
    /// de contexte périmée est pire qu'une absence de réponse — elle ferait proposer une action
    /// sur un fichier que l'utilisateur a quitté depuis longtemps.
    /// </summary>
    public bool IsDeferrable => false;

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        var request = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "work_context",
            Payload = new { },
        };

        var result = await _daemon.SendActionAsync(request, ct);

        if (!result.Success)
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);

        return ApiResponse<ToolResult>.SuccessResponse(
            ToolResult.SuccessResult(JsonSerializer.Serialize(result.Data?.Data), Name));
    }

    /// <summary>
    /// Widget PERMANENT — le premier du HUD.
    ///
    /// L'analyse du titre vit ICI et non dans le daemon : chaque éditeur a son format et ils
    /// changent au fil des versions. Ajuster côté backend prend deux minutes de déploiement ;
    /// côté daemon il faudrait repasser sur la machine de l'utilisateur.
    /// </summary>
    public HudCard? BuildCard(ToolResult result)
    {
        if (result.Data is not string json) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var active = root.TryGetProperty("active", out var a) && a.GetBoolean();
            var application = root.TryGetProperty("application", out var app) ? app.GetString() : null;
            var title = root.TryGetProperty("windowTitle", out var t) ? t.GetString() : null;

            if (!active)
            {
                return new HudCard
                {
                    Id = "work.context",
                    Kind = HudCardKind.Status,
                    Lifetime = HudCardLifetime.Pinned,
                    Label = "Contexte",
                    Value = "session inactive",
                    State = HudCardState.Neutral,
                };
            }

            var (file, project) = Decompose(title);

            var items = new List<HudCardItem>();
            if (project is not null) items.Add(new HudCardItem { Label = "Projet", Value = project });
            if (application is not null) items.Add(new HudCardItem { Label = "Application", Value = application });

            return new HudCard
            {
                Id = "work.context",
                Kind = HudCardKind.Status,
                Lifetime = HudCardLifetime.Pinned,
                Label = "Contexte",

                // Le FICHIER en valeur principale : c'est l'unité de travail réelle, pas
                // l'application. « useVAD.ts » dit quelque chose, « Code » ne dit rien.
                Value = file ?? application ?? "inconnu",
                State = HudCardState.Ok,
                Items = items.Count > 0 ? items : null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extrait fichier et projet d'un titre de fenêtre.
    ///
    /// Les éditeurs séparent par « - » : VS Code rend « useVAD.ts - ShiftCore - Visual Studio
    /// Code », Visual Studio « Program.cs - Orion.Api - Microsoft Visual Studio ». Le premier
    /// segment est le document, le dernier le logiciel, celui du milieu le projet.
    ///
    /// Renvoie null plutôt que de deviner quand le format ne correspond pas : afficher un mauvais
    /// nom de fichier ferait proposer une action sur le mauvais fichier.
    /// </summary>
    private static (string? File, string? Project) Decompose(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return (null, null);

        var parts = title.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return (null, null);

        // Une pastille « ● » précède le nom quand le fichier a des modifications non enregistrées.
        var file = parts[0].TrimStart('●', '*', ' ');
        var project = parts.Length >= 3 ? parts[^2] : null;

        return (file, project);
    }
}
