using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.Memory;

/// <summary>
/// Enregistre ce que l'utilisateur pense d'un type d'alerte proactive.
///
/// C'est le seul mécanisme par lequel ORION peut apprendre à SE TAIRE. Sans lui, un signal
/// que l'utilisateur ignore revient indéfiniment — et c'est ainsi qu'on finit par désactiver
/// un assistant plutôt que de le corriger.
/// </summary>
public class ProactiveFeedbackTool : ITool
{
    private readonly IProactiveLearningService _apprentissage;
    private readonly ILogger<ProactiveFeedbackTool> _logger;

    public ProactiveFeedbackTool(IProactiveLearningService apprentissage, ILogger<ProactiveFeedbackTool> logger)
    {
        _apprentissage = apprentissage;
        _logger = logger;
    }

    public string Name => "proactive_feedback";

    public string Description =>
        "Enregistre l'avis de l'utilisateur sur un type d'alerte proactive. À appeler quand il dit "
        + "qu'une alerte l'agace, ne lui sert à rien, ou au contraire qu'elle lui a été utile. "
        + "Un type rejeté plusieurs fois cesse d'interrompre.";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["pattern"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Type d'alerte concerné : high_ram, high_cpu, meal_time, "
                                  + "break_time, night_time, skip_meal, overwork, unpushed_work, "
                                  + "service_down, vps_down"
            },
            ["utile"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "false si l'utilisateur ne veut plus de cette alerte, true si elle lui a servi"
            },
            ["motif"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Ce que l'utilisateur a dit, dans ses mots — pour retrouver le pourquoi plus tard"
            }
        },
        ["required"] = new JsonArray { "pattern", "utile" }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        var pattern = input["pattern"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(pattern))
            return ApiResponse<ToolResult>.ErrorResponse("Parametre 'pattern' requis", 400);

        // Sans avis explicite, on ne devine pas : enregistrer un rejet par défaut ferait taire
        // ORION sur un malentendu.
        if (input["utile"] is null)
            return ApiResponse<ToolResult>.ErrorResponse("Parametre 'utile' requis (true ou false)", 400);

        var utile = input["utile"]!.GetValue<bool>();
        var motif = input["motif"]?.GetValue<string>();

        var resultat = await _apprentissage.EnregistrerRetourAsync(pattern, utile, motif, ct);

        if (!resultat.Success)
            return ApiResponse<ToolResult>.ErrorResponse(resultat.Message ?? "Retour non enregistre", resultat.StatusCode);

        _logger.LogInformation("[proactive_feedback] {Pattern} — utile: {Utile}", pattern, utile);

        return ApiResponse<ToolResult>.SuccessResponse(
            ToolResult.SuccessResult(new
            {
                pattern,
                utile,
                effet = utile
                    ? "Cette alerte reste active."
                    : "Cette alerte va se faire plus discrete, puis se taire si tu la refuses encore."
            }, Name));
    }
}
