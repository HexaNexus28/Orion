using Microsoft.EntityFrameworkCore;
using Orion.Core.Entities;
using Orion.Core.Interfaces.Repositories;
using Orion.Data.Context;

namespace Orion.Data.Repositories;

public class AuditLogRepository : GenericRepository<AuditLog, Guid>, IAuditLogRepository
{
    public AuditLogRepository(OrionDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, string entityId, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<AuditLog>> GetByUserAsync(string userId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var query = _dbSet.AsNoTracking().Where(a => a.UserId == userId);
        
        if (from.HasValue)
            query = query.Where(a => a.Timestamp >= from.Value);
        
        if (to.HasValue)
            query = query.Where(a => a.Timestamp <= to.Value);
        
        return await query
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<AuditLog>> GetByActionAsync(string action, DateTime? from = null, CancellationToken ct = default)
    {
        var query = _dbSet.AsNoTracking().Where(a => a.Action == action);
        
        if (from.HasValue)
            query = query.Where(a => a.Timestamp >= from.Value);
        
        return await query
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<AuditLog>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(a => a.CorrelationId == correlationId)
            .OrderBy(a => a.Timestamp)
            .ToListAsync(ct);
    }
}
