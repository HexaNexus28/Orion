using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Orion.Core.Interfaces.Repositories;
using Orion.Data.Context;
using Orion.Data.Repositories;

namespace Orion.Data.UnitOfWork;

public class UnitOfWork : IUnitOfWork
{
    private readonly OrionDbContext _context;
    private IDbContextTransaction? _currentTransaction;

    private IConversationRepository? _conversations;
    private IMessageRepository? _messages;
    private IMemoryRepository? _memory;
    private IUserProfileRepository? _userProfile;
    private IAuditLogRepository? _auditLogs;
    private IBehaviorPatternRepository? _behaviorPatterns;

    public UnitOfWork(OrionDbContext context)
    {
        _context = context;
    }

    public IConversationRepository Conversations => 
        _conversations ??= new ConversationRepository(_context);
    
    public IMessageRepository Messages => 
        _messages ??= new MessageRepository(_context);
    
    public IMemoryRepository Memory => 
        _memory ??= new MemoryRepository(_context);
    
    public IUserProfileRepository UserProfile => 
        _userProfile ??= new UserProfileRepository(_context);
    
    public IAuditLogRepository AuditLogs => 
        _auditLogs ??= new AuditLogRepository(_context);
    
    public IBehaviorPatternRepository BehaviorPatterns => 
        _behaviorPatterns ??= new BehaviorPatternRepository(_context);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction != null)
            throw new InvalidOperationException("A transaction is already in progress");

        _currentTransaction = await _context.Database.BeginTransactionAsync(ct);
    }

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
            
            if (_currentTransaction != null)
            {
                await _currentTransaction.CommitAsync(ct);
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
        catch
        {
            await RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction != null)
        {
            await _currentTransaction.RollbackAsync(ct);
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        
        try
        {
            var result = await action();
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public void Dispose()
    {
        _currentTransaction?.Dispose();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_currentTransaction != null)
            await _currentTransaction.DisposeAsync();
        
        await _context.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
