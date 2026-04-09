using Orion.Core.Entities;

namespace Orion.Core.Interfaces.Repositories;

/// <summary>
/// Repository pour les logs d'audit
/// </summary>
public interface IAuditLogRepository : IGenericRepository<AuditLog, Guid>
{
    Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, string entityId, CancellationToken ct = default);
    Task<IEnumerable<AuditLog>> GetByUserAsync(string userId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<IEnumerable<AuditLog>> GetByActionAsync(string action, DateTime? from = null, CancellationToken ct = default);
    Task<IEnumerable<AuditLog>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default);
}
