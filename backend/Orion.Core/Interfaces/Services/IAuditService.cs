namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Service pour la traçabilité et l'audit
/// </summary>
public interface IAuditService
{
    Task LogAsync(string entityType, string entityId, string action, 
        string? oldValues = null, string? newValues = null, 
        string? metadata = null, TimeSpan? duration = null, 
        bool success = true, string? errorMessage = null,
        CancellationToken ct = default);
    
    Task LogEntityCreateAsync<T>(T entity, string? metadata = null, CancellationToken ct = default) where T : class;
    Task LogEntityUpdateAsync<T>(T oldEntity, T newEntity, string? metadata = null, CancellationToken ct = default) where T : class;
    Task LogEntityDeleteAsync<T>(T entity, string? metadata = null, CancellationToken ct = default) where T : class;
    
    Task LogToolCallAsync(string toolName, string input, string? output = null, 
        TimeSpan? duration = null, bool success = true, string? errorMessage = null,
        CancellationToken ct = default);
    
    Task LogLLMCallAsync(string provider, string model, string prompt, 
        string? response = null, int? tokensUsed = null, TimeSpan? duration = null,
        bool success = true, string? errorMessage = null,
        CancellationToken ct = default);
    
    string GetCorrelationId();
    void SetCorrelationId(string correlationId);
}
