using Orion.Core.Entities;

namespace Orion.Core.Interfaces.Repositories;

public interface IDeferredActionRepository : IGenericRepository<DeferredAction, Guid>
{
    /// <summary>
    /// Ce qui attend encore : `pending` + `awaiting_confirmation`, non expiré.
    /// C'est ce que l'UI affiche et ce que l'utilisateur peut annuler.
    /// </summary>
    Task<IEnumerable<DeferredAction>> GetLiveAsync(CancellationToken ct = default);

    /// <summary>Les `pending` non expirées, dans l'ordre où elles ont été demandées.</summary>
    Task<IEnumerable<DeferredAction>> GetDrainableAsync(CancellationToken ct = default);

    /// <summary>
    /// Passe en `expired` tout ce qui a dépassé son TTL, et rend le nombre de lignes touchées.
    /// L'expiration s'appuie sur `expires_at` en base, donc elle reste correcte même si le
    /// backend est resté éteint pendant trois jours.
    /// </summary>
    Task<int> ExpireStaleAsync(DateTime now, CancellationToken ct = default);

    /// <summary>Historique récent, tous états confondus — pour que l'UI montre aussi ce qui a été fait.</summary>
    Task<IEnumerable<DeferredAction>> GetRecentAsync(int limit, CancellationToken ct = default);
}
