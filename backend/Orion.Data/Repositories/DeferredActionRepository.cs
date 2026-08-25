using Microsoft.EntityFrameworkCore;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Repositories;
using Orion.Data.Context;

namespace Orion.Data.Repositories;

public class DeferredActionRepository : GenericRepository<DeferredAction, Guid>, IDeferredActionRepository
{
    private static readonly DeferredActionStatus[] EtatsVivants =
    {
        DeferredActionStatus.Pending,
        DeferredActionStatus.AwaitingConfirmation
    };

    public DeferredActionRepository(OrionDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<DeferredAction>> GetLiveAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _dbSet
            .Where(a => EtatsVivants.Contains(a.Status) && a.ExpiresAt > now)
            .OrderBy(a => a.RequestedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<DeferredAction>> GetDrainableAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _dbSet
            .Where(a => a.Status == DeferredActionStatus.Pending && a.ExpiresAt > now)
            .OrderBy(a => a.RequestedAt)
            .ToListAsync(ct);
    }

    public async Task<int> ExpireStaleAsync(DateTime now, CancellationToken ct = default)
    {
        // Chargées puis modifiées, pas d'`ExecuteUpdate` : la file est bornée par construction
        // (ce qu'une personne demande en une nuit, TTL 24 h), donc la requête en lot n'achèterait
        // rien — et elle rendrait l'expiration intestable, faute de support dans le fournisseur
        // en mémoire. Le SaveChanges appartient à l'appelant, comme partout ailleurs ici.
        var perimees = await _dbSet
            .Where(a => EtatsVivants.Contains(a.Status) && a.ExpiresAt <= now)
            .ToListAsync(ct);

        foreach (var action in perimees)
        {
            action.Status = DeferredActionStatus.Expired;
            action.ResolvedAt = now;
        }

        return perimees.Count;
    }

    public async Task<IEnumerable<DeferredAction>> GetRecentAsync(int limit, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .OrderByDescending(a => a.RequestedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
