using Orion.Core.Entities;

namespace Orion.Core.Interfaces.Repositories;

public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    IConversationRepository Conversations { get; }
    IMessageRepository Messages { get; }
    IMemoryRepository Memory { get; }
    IUserProfileRepository UserProfile { get; }
    IAuditLogRepository AuditLogs { get; }
    IBehaviorPatternRepository BehaviorPatterns { get; }
    
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct = default);
}
