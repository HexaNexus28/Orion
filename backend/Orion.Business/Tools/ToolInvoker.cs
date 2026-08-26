using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Internal.Tools;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
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

        // GARDE-FOU DES ACTIONS IRREVERSIBLES.
        //
        // Avant, `IsDestructive` ne servait QUE lorsque le PC etait eteint : PC allume, un
        // run_script, un write_file ou un kill_process partait immediatement. Le seul garde-fou
        // etait une phrase du prompt systeme demandant au modele de confirmer d abord —
        // c est-a-dire une SUGGESTION, pas une regle. Un modele qui decide d agir agit.
        //
        // Ce n est pas un risque theorique : ORION lit le web (web_browse, web_fetch). Une page
        // peut contenir des instructions qui detournent le modele, et la requete resultante est
        // parfaitement AUTHENTIFIEE — aucun controle d acces ne peut l arreter. Seule une regle
        // placee ici, apres la decision du modele, le peut.
        //
        // On reutilise la file existante : elle sait deja demander confirmation, et le drainage
        // refuse deja de rejouer une action destructive tout seul. Rien de nouveau a inventer.
        if (tool.IsDestructive)
        {
            return await EnqueueAsync(tool, input, context, ct);
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
        // DEUX raisons distinctes de ne pas executer tout de suite, et le message doit dire
        // laquelle : « ton PC est eteint » alors que le PC tourne serait un mensonge, et
        // l utilisateur chercherait un probleme qui n existe pas.
        var pcEteint = tool.RequiresDaemon && !_daemon.IsConnected;

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
            ExpiresAt = now.AddHours(Math.Max(1, _options.DeferredTtlHours)),

            // PC allume + action destructive : ce n est pas le PC qu on attend, c est TOI.
            // L etat le dit explicitement, sinon le drainage au reveil la traiterait comme une
            // action simplement en retard.
            // Destructif ET PC allume -> c est TOI qu on attend : AwaitingConfirmation.
            // PC eteint -> c est le PC qu on attend : Pending. Le drainage passera l action en
            // AwaitingConfirmation a son reveil, et lui seul sait si le PC est revenu. Marquer
            // AwaitingConfirmation des maintenant la sortirait de la file de drainage, et une
            // confirmation donnee PC eteint echouerait faute de daemon.
            Status = tool.IsDestructive && !pcEteint
                ? DeferredActionStatus.AwaitingConfirmation
                : DeferredActionStatus.Pending
        };

        await _unitOfWork.DeferredActions.AddAsync(action, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[ToolInvoker] {ToolName} en attente ({Raison}) — expire le {Expiration:u}",
            tool.Name, action.IsDestructive ? "confirmation" : "PC éteint", action.ExpiresAt);

        var message = (action.IsDestructive, pcEteint) switch
        {
            (true, false) => $"« {tool.Name} » modifie l'état de ta machine — je ne le lance pas "
                           + "sans ton accord. Confirme et je l'exécute.",
            (true, true)  => $"Ton PC est éteint. J'ai noté « {tool.Name} » et je te le redemanderai "
                           + "à son réveil avant de le lancer.",
            _             => $"Ton PC est éteint. J'ai mis « {tool.Name} » en file et je le fais "
                           + "dès qu'il revient."
        };

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
