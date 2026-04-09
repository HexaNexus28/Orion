using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Orion.Core.Entities;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// Service d'audit pour traçabilité des actions
/// </summary>
public class AuditService : IAuditService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AuditService> _logger;
    private string? _currentCorrelationId;
    private static readonly AsyncLocal<string?> _correlationId = new AsyncLocal<string?>();

    public AuditService(IUnitOfWork unitOfWork, ILogger<AuditService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task LogAsync(string entityType, string entityId, string action, 
        string? oldValues = null, string? newValues = null, 
        string? metadata = null, TimeSpan? duration = null, 
        bool success = true, string? errorMessage = null,
        CancellationToken ct = default)
    {
        try
        {
            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                UserId = GetCurrentUserId(),
                UserName = GetCurrentUserName(),
                OldValues = oldValues,
                NewValues = newValues,
                Metadata = metadata ?? BuildMetadata(),
                DurationMs = duration.HasValue ? (int)duration.Value.TotalMilliseconds : null,
                Success = success,
                ErrorMessage = errorMessage,
                Timestamp = DateTime.UtcNow,
                CorrelationId = GetCorrelationId()
            };

            await _unitOfWork.AuditLogs.AddAsync(auditLog, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogDebug("Audit log created: {Action} on {EntityType}:{EntityId}", 
                action, entityType, entityId);
        }
        catch (Exception ex)
        {
            // Ne jamais faire échouer une opération à cause de l'audit
            _logger.LogError(ex, "Failed to create audit log for {Action} on {EntityType}:{EntityId}", 
                action, entityType, entityId);
        }
    }

    public async Task LogEntityCreateAsync<T>(T entity, string? metadata = null, CancellationToken ct = default) where T : class
    {
        var entityType = typeof(T).Name;
        var entityId = GetEntityId(entity);
        var newValues = JsonSerializer.Serialize(entity);

        await LogAsync(entityType, entityId, "Create", 
            oldValues: null, newValues: newValues, 
            metadata: metadata, success: true, ct: ct);
    }

    public async Task LogEntityUpdateAsync<T>(T oldEntity, T newEntity, string? metadata = null, CancellationToken ct = default) where T : class
    {
        var entityType = typeof(T).Name;
        var entityId = GetEntityId(newEntity);
        var oldValues = JsonSerializer.Serialize(oldEntity);
        var newValues = JsonSerializer.Serialize(newEntity);

        await LogAsync(entityType, entityId, "Update", 
            oldValues: oldValues, newValues: newValues, 
            metadata: metadata, success: true, ct: ct);
    }

    public async Task LogEntityDeleteAsync<T>(T entity, string? metadata = null, CancellationToken ct = default) where T : class
    {
        var entityType = typeof(T).Name;
        var entityId = GetEntityId(entity);
        var oldValues = JsonSerializer.Serialize(entity);

        await LogAsync(entityType, entityId, "Delete", 
            oldValues: oldValues, newValues: null, 
            metadata: metadata, success: true, ct: ct);
    }

    public async Task LogToolCallAsync(string toolName, string input, string? output = null, 
        TimeSpan? duration = null, bool success = true, string? errorMessage = null,
        CancellationToken ct = default)
    {
        var metadata = JsonSerializer.Serialize(new { Input = input, Output = output });
        
        await LogAsync("Tool", toolName, "ToolCall", 
            oldValues: null, newValues: null, 
            metadata: metadata, duration: duration, 
            success: success, errorMessage: errorMessage, ct: ct);
    }

    public async Task LogLLMCallAsync(string provider, string model, string prompt, 
        string? response = null, int? tokensUsed = null, TimeSpan? duration = null,
        bool success = true, string? errorMessage = null,
        CancellationToken ct = default)
    {
        var metadata = JsonSerializer.Serialize(new { 
            Provider = provider, 
            Model = model, 
            PromptLength = prompt?.Length,
            ResponseLength = response?.Length,
            TokensUsed = tokensUsed 
        });

        await LogAsync("LLM", $"{provider}:{model}", "LLMCall", 
            oldValues: null, newValues: null, 
            metadata: metadata, duration: duration, 
            success: success, errorMessage: errorMessage, ct: ct);
    }

    public string GetCorrelationId()
    {
        return _correlationId.Value ?? (_currentCorrelationId ??= Guid.NewGuid().ToString("N"));
    }

    public void SetCorrelationId(string correlationId)
    {
        _currentCorrelationId = correlationId;
        _correlationId.Value = correlationId;
    }

    private string GetEntityId<T>(T entity) where T : class
    {
        // Essaye de trouver une propriété Id
        var idProperty = typeof(T).GetProperty("Id") ?? typeof(T).GetProperty("id");
        if (idProperty != null)
        {
            var value = idProperty.GetValue(entity);
            return value?.ToString() ?? Guid.NewGuid().ToString("N");
        }
        return Guid.NewGuid().ToString("N");
    }

    private string? GetCurrentUserId()
    {
        // À implémenter avec l'authentification
        // return _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        return null;
    }

    private string? GetCurrentUserName()
    {
        // À implémenter avec l'authentification
        return null;
    }

    private string? BuildMetadata()
    {
        try
        {
            return JsonSerializer.Serialize(new
            {
                MachineName = Environment.MachineName,
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId
            });
        }
        catch
        {
            return null;
        }
    }
}
