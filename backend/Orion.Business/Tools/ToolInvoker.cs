using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Internal.Tools;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools;

/// <summary>
/// Le seul endroit qui décide si un outil s'exécute, se diffère, ou se refuse.
///
/// Trois issues quand le PC est éteint, et une seule est nouvelle :
///   • outil sans daemon .............. s'exécute, rien ne change
///   • daemon requis + différable ..... enfilé, ORION promet et tiendra
///   • daemon requis + non différable . refus HONNÊTE, jamais un faux succès
///
/// La troisième issue est la raison d'être de tout ça : avant, les trois cas rendaient le même
/// « Daemon non connecté » sec, qui faisait paraître ORION cassé alors qu'il fonctionnait.
/// </summary>
public class ToolInvoker : IToolInvoker
{
    private readonly IToolRegistry _registry;
    private readonly IDaemonClient _daemon;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DaemonOptions _options;
    private readonly ILogger<ToolInvoker> _logger;

    public ToolInvoker(
        IToolRegistry registry,
        IDaemonClient daemon,
        IUnitOfWork unitOfWork,
        IOptions<DaemonOptions> options,
        ILogger<ToolInvoker> logger)
    {
        _registry = registry;
        _daemon = daemon;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> InvokeAsync(
        string toolName,
        JsonObject input,
        ToolInvocationContext context,
        CancellationToken ct = default)
    {
        var tool = _registry.GetTool(toolName);
        if (tool is null)
        {
            _logger.LogWarning("[ToolInvoker] Outil inconnu : {ToolName}", toolName);
            return ApiResponse<ToolResult>.NotFoundResponse($"Outil '{toolName}' introuvable");
        }

        if (tool.RequiresDaemon && !_daemon.IsConnected)
        {
            return tool.IsDeferrable
                ? await EnqueueAsync(tool, input, context, ct)
                : RefuserFranchement(tool);
        }

        return await ExecuteAsync(tool, input, ct);
    }

    public async Task<ApiResponse<ToolResult>> InvokeNowAsync(
        string toolName,
        JsonObject input,
        CancellationToken ct = default)
    {
        var tool = _registry.GetTool(toolName);
        if (tool is null)
        {
            return ApiResponse<ToolResult>.NotFoundResponse($"Outil '{toolName}' introuvable");
        }

        return await ExecuteAsync(tool, input, ct);
    }

    private async Task<ApiResponse<ToolResult>> ExecuteAsync(ITool tool, JsonObject input, CancellationToken ct)
    {
        try
        {
            var reponse = await tool.ExecuteAsync(input, ct);

            // La carte est construite ICI, et nulle part ailleurs : ToolInvoker est deja le
            // seul endroit qui execute un outil. La produire dans la boucle agent ou dans un
            // controleur voudrait dire la reconstruire a chaque nouveau chemin d appel.
            //
            // Un outil qui leve en fabriquant sa carte ne doit pas faire echouer son propre
            // resultat : la carte est un affichage, pas le travail demande.
            if (reponse.Data is { Success: true } resultat)
            {
                try
                {
                    resultat.Card = tool.BuildCard(resultat);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ToolInvoker] {ToolName} : carte HUD non construite", tool.Name);
                }
            }

            return reponse;
        }
        catch (Exception ex)
        {
            // Un outil qui lève ne doit pas faire tomber la boucle agent : le modèle doit
            // recevoir l'échec comme un résultat, pour pouvoir en parler ou changer de plan.
            _logger.LogError(ex, "[ToolInvoker] {ToolName} a levé", tool.Name);
            return ApiResponse<ToolResult>.SuccessResponse(ToolResult.FromException(ex, tool.Name));
        }
    }

    private async Task<ApiResponse<ToolResult>> EnqueueAsync(
        ITool tool,
        JsonObject input,
        ToolInvocationContext context,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var action = new DeferredAction
        {
            ToolName = tool.Name,
            Arguments = input.ToJsonString(),
            // Figé ici, jamais relu depuis le code de l'outil : l'action garde le régime sous
            // lequel elle a été demandée, même si le drapeau change d'ici son exécution.
            IsDestructive = tool.IsDestructive,
            Origin = context.Origin,
            ConversationId = context.ConversationId,
            RequestedBy = context.RequestedBy,
            RequestedAt = now,
            ExpiresAt = now.AddHours(Math.Max(1, _options.DeferredTtlHours))
        };

        await _unitOfWork.DeferredActions.AddAsync(action, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[ToolInvoker] {ToolName} différé (destructif: {Destructif}) — expire le {Expiration:u}",
            tool.Name, action.IsDestructive, action.ExpiresAt);

        var message = action.IsDestructive
            ? $"Ton PC est éteint. J'ai noté « {tool.Name} » et je te le redemanderai à son réveil "
              + "avant de le lancer — je ne rejoue pas une action qui modifie l'état sans te la remontrer."
            : $"Ton PC est éteint. J'ai mis « {tool.Name} » en file et je le fais dès qu'il revient.";

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            new
            {
                deferred = true,
                actionId = action.Id,
                requiresConfirmation = action.IsDestructive,
                expiresAt = action.ExpiresAt,
                message
            },
            tool.Name));
    }

    private ApiResponse<ToolResult> RefuserFranchement(ITool tool)
    {
        _logger.LogInformation("[ToolInvoker] {ToolName} refusé — PC éteint et sans valeur différée", tool.Name);

        // Volontairement un échec, pas une file : différer une lecture produirait une réponse
        // qui décrit l'état d'hier. Mieux vaut le dire tout de suite que répondre à côté demain.
        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.ErrorResult(
            $"Ton PC est éteint, donc « {tool.Name} » ne peut pas répondre. Le mettre en attente "
            + "n'aurait pas de sens : la réponse décrirait l'état d'hier. Redemande-le quand ton PC sera allumé.",
            errorCode: "daemon_offline",
            toolName: tool.Name));
    }
}
