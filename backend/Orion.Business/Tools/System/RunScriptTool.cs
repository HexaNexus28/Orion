using System.Text.Json;
using System.Text.Json.Nodes;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class RunScriptTool : ITool
{
    private readonly IDaemonClient _daemon;

    public RunScriptTool(IDaemonClient daemon)
    {
        _daemon = daemon;
    }

    public string Name => "run_script";
    public string Description => "Exécute un script PowerShell sur le PC Windows et retourne la sortie";

    public bool RequiresDaemon => true;
    public bool IsDestructive => true;

    // PAS différable, et c'est une correction issue de l'essai réel du 2026-08-21.
    //
    // `list_files` est retiré du catalogue quand le PC est éteint — une liste de fichiers
    // d'hier ne vaut rien. Mais le modèle a alors SUBSTITUÉ `run_script` avec un
    // `Get-ChildItem` : la lecture refusée par la porte est repassée par la fenêtre, et
    // l'utilisateur s'est vu promettre pour demain matin une réponse qu'il voulait tout de
    // suite. Filtrer les lectures ne sert à rien tant qu'un exécuteur générique reste ouvert.
    //
    // Un script est arbitraire : ORION ne peut pas savoir s'il lit ou s'il écrit, donc pas
    // savoir si le différer garde un sens. C'est exactement le critère qui doit fermer la
    // porte — on ne diffère pas ce qu'on ne comprend pas. PC éteint ⇒ refus franc.

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["script"] = new JsonObject { ["type"] = "string", ["description"] = "Script PowerShell à exécuter" },
            ["workingDir"] = new JsonObject { ["type"] = "string", ["description"] = "Répertoire de travail (optionnel)" }
        },
        ["required"] = new JsonArray { "script" }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {

        var script = input["script"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(script))
            return ApiResponse<ToolResult>.ErrorResponse("Paramètre script requis", 400);

        var workingDir = input["workingDir"]?.GetValue<string>();

        var result = await _daemon.SendActionAsync(new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "run_script",
            Payload = new { script, workingDir }
        }, ct);

        if (!result.Success)
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            JsonSerializer.Serialize(result.Data?.Data), Name));
    }
}
