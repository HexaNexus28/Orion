namespace Orion.Core.DTOs.Responses;

public class ToolResult
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; } // machine-readable error
    
    // Execution metadata
    public string? ToolName { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public int? RetryCount { get; set; }
    
    // Source tracking
    public string? Source { get; set; } // 'local', 'api', 'daemon'
    public Dictionary<string, object>? Metadata { get; set; }

    public static ToolResult SuccessResult(object? data = null, string? toolName = null)
        => new() { Success = true, Data = data, ToolName = toolName };

    public static ToolResult ErrorResult(string error, string? errorCode = null, string? toolName = null)
        => new() { Success = false, Error = error, ErrorCode = errorCode, ToolName = toolName };
    
    public static ToolResult FromException(Exception ex, string toolName)
        => new() 
        { 
            Success = false, 
            Error = ex.Message, 
            ErrorCode = ex.GetType().Name,
            ToolName = toolName,
            Metadata = new Dictionary<string, object> 
            { 
                ["stackTrace"] = ex.StackTrace ?? "N/A",
                ["innerException"] = ex.InnerException?.Message ?? "N/A"
            }
        };
}
