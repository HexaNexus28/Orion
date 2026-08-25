using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Daemon;

/// <summary>
/// Vide la file quand le PC revient.
///
/// Deux régimes, décidés à l'enfilement et jamais renégociés :
///   • non destructif → exécuté tout de suite, l'utilisateur est informé après coup ;
///   • destructif ..... passé en attente de confirmation. Il s'exécuterait sur un état de
///     machine que l'utilisateur n'a pas vu — c'est précisément ce qu'on refuse. Il se
///     redemande, il ne se rejoue pas.
/// </summary>
public class DeferredActionService : IDeferredActionService
{
    private const int HistoriqueRecent = 20;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IToolInvoker _invoker;
    private readonly ILogger<DeferredActionService> _logger;

    public DeferredActionService(
        IUnitOfWork unitOfWork,
        IToolInvoker invoker,
        ILogger<DeferredActionService> logger)
    {
        _unitOfWork = unitOfWork;
        _invoker = invoker;
        _logger = logger;
    }

    public async Task<ApiResponse<List<DeferredActionDto>>> GetQueueAsync(CancellationToken ct = default)
    {
        var recentes = await _unitOfWork.DeferredActions.GetRecentAsync(HistoriqueRecent, ct);
        return ApiResponse<List<DeferredActionDto>>.SuccessResponse(recentes.Select(Vers).ToList());
    }

    public async Task<ApiResponse<int>> ExpireStaleAsync(CancellationToken ct = default)
    {
        var expirees = await _unitOfWork.DeferredActions.ExpireStaleAsync(DateTime.UtcNow, ct);
        if (expirees > 0)
        {
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("[File différée] {Count} action(s) expirée(s) sans exécution", expirees);
        }
        return ApiResponse<int>.SuccessResponse(expirees);
    }

    public async Task<ApiResponse<DeferredDrainReport>> DrainAsync(CancellationToken ct = default)
    {
        var rapport = new DeferredDrainReport();

        // D'abord périmer, ensuite exécuter : sinon une action qui vient d'expirer partirait
        // quand même, parce qu'elle était encore `pending` au moment de la lecture.
        rapport.Expired = (await ExpireStaleAsync(ct)).Data;

        var aTraiter = (await _unitOfWork.DeferredActions.GetDrainableAsync(ct)).ToList();
        if (aTraiter.Count == 0)
        {
            return ApiResponse<DeferredDrainReport>.SuccessResponse(rapport);
        }

        _logger.LogInformation("[File différée] {Count} action(s) à traiter au réveil du PC", aTraiter.Count);

        foreach (var action in aTraiter)
        {
            if (action.IsDestructive)
            {
                action.Status = DeferredActionStatus.AwaitingConfirmation;
                rapport.AwaitingConfirmation.Add(Vers(action));
                continue;
            }

            await ExecuterAsync(action, ct);

            if (action.Status == DeferredActionStatus.Executed) rapport.Executed.Add(Vers(action));
            else rapport.Failed.Add(Vers(action));
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return ApiResponse<DeferredDrainReport>.SuccessResponse(rapport);
    }

    public async Task<ApiResponse<DeferredActionDto>> ConfirmAsync(Guid id, CancellationToken ct = default)
    {
        var action = await _unitOfWork.DeferredActions.GetByIdAsync(id, ct);
        if (action is null)
        {
            return ApiResponse<DeferredActionDto>.NotFoundResponse("Action introuvable");
        }

        if (action.Status != DeferredActionStatus.AwaitingConfirmation)
        {
            return ApiResponse<DeferredActionDto>.ErrorResponse(
                $"Cette action est « {action.Status.ToSlug()} », il n'y a plus rien à confirmer.", 409);
        }

        if (action.ExpiresAt <= DateTime.UtcNow)
        {
            action.Status = DeferredActionStatus.Expired;
            action.ResolvedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<DeferredActionDto>.ErrorResponse(
                "Cette action a expiré avant d'être confirmée — redemande-la si elle est encore d'actualité.", 410);
        }

        await ExecuterAsync(action, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return ApiResponse<DeferredActionDto>.SuccessResponse(Vers(action));
    }

    public async Task<ApiResponse<DeferredActionDto>> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var action = await _unitOfWork.DeferredActions.GetByIdAsync(id, ct);
        if (action is null)
        {
            return ApiResponse<DeferredActionDto>.NotFoundResponse("Action introuvable");
        }

        var vivante = action.Status is DeferredActionStatus.Pending or DeferredActionStatus.AwaitingConfirmation;
        if (!vivante)
        {
            return ApiResponse<DeferredActionDto>.ErrorResponse(
                $"Cette action est déjà « {action.Status.ToSlug()} », elle ne peut plus être annulée.", 409);
        }

        action.Status = DeferredActionStatus.Cancelled;
        action.ResolvedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("[File différée] {ToolName} annulée par l'utilisateur", action.ToolName);
        return ApiResponse<DeferredActionDto>.SuccessResponse(Vers(action));
    }

    /// <summary>
    /// Rejoue l'appel par le chemin normal, en court-circuitant la mise en file : le daemon
    /// vient de revenir, re-différer ferait tourner l'action en rond jusqu'à son expiration.
    /// </summary>
    private async Task ExecuterAsync(DeferredAction action, CancellationToken ct)
    {
        JsonObject arguments;
        try
        {
            arguments = JsonNode.Parse(action.Arguments)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            action.Status = DeferredActionStatus.Failed;
            action.Error = $"Arguments illisibles : {ex.Message}";
            action.ResolvedAt = DateTime.UtcNow;
            return;
        }

        var resultat = await _invoker.InvokeNowAsync(action.ToolName, arguments, ct);
        var reussi = resultat.Success && resultat.Data is { Success: true };

        action.Status = reussi ? DeferredActionStatus.Executed : DeferredActionStatus.Failed;
        action.ResolvedAt = DateTime.UtcNow;

        if (reussi)
        {
            action.Result = JsonSerializer.Serialize(resultat.Data!.Data);
            action.Error = null;
            _logger.LogInformation("[File différée] {ToolName} exécutée au réveil", action.ToolName);
        }
        else
        {
            action.Error = resultat.Data?.Error ?? resultat.Message ?? "Échec inconnu";
            _logger.LogWarning("[File différée] {ToolName} a échoué : {Erreur}", action.ToolName, action.Error);
        }
    }

    private static DeferredActionDto Vers(DeferredAction a) => new()
    {
        Id = a.Id,
        ToolName = a.ToolName,
        Arguments = a.Arguments,
        Status = a.Status.ToSlug(),
        IsDestructive = a.IsDestructive,
        Origin = a.Origin,
        RequestedBy = a.RequestedBy,
        RequestedAt = a.RequestedAt,
        ExpiresAt = a.ExpiresAt,
        ResolvedAt = a.ResolvedAt,
        Result = a.Result,
        Error = a.Error
    };
}
