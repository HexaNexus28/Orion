using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// La file des actions que le PC éteint n'a pas pu exécuter.
///
/// L'enfilement ne passe PAS par ici : il vit dans <c>IToolInvoker</c>, seul endroit qui sache
/// qu'un outil vient d'échouer faute de PC. Y ajouter un <c>EnqueueAsync</c> créerait un cycle
/// (l'invoker enfile, la file exécute) pour un gain nul.
///
/// Ce service ne connaît ni le WebSocket du daemon ni le canal de notification : il décide QUOI,
/// la couche API décide comment le dire.
/// </summary>
public interface IDeferredActionService
{
    /// <summary>Ce qui attend, puis l'historique récent — l'UI montre les deux.</summary>
    Task<ApiResponse<List<DeferredActionDto>>> GetQueueAsync(CancellationToken ct = default);

    /// <summary>
    /// Le PC est revenu : exécuter les non-destructives, mettre les destructives en attente de
    /// confirmation, expirer ce qui a dépassé son TTL.
    /// </summary>
    Task<ApiResponse<DeferredDrainReport>> DrainAsync(CancellationToken ct = default);

    /// <summary>L'utilisateur a confirmé une action destructive : elle part maintenant.</summary>
    Task<ApiResponse<DeferredActionDto>> ConfirmAsync(Guid id, CancellationToken ct = default);

    Task<ApiResponse<DeferredActionDto>> CancelAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Passe en `expired` ce qui a dépassé son TTL, sans rien exécuter. Appelé périodiquement :
    /// une action doit pouvoir mourir de vieillesse même si le PC ne se rallume jamais.
    /// </summary>
    Task<ApiResponse<int>> ExpireStaleAsync(CancellationToken ct = default);
}
