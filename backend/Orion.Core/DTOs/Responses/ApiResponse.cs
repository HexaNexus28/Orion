namespace Orion.Core.DTOs.Responses;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public int StatusCode { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }

    // Factory methods - ShadowCat pattern
    public static ApiResponse<T> SuccessResponse(T data, string? message = null)
        => new() { Success = true, Data = data, Message = message, StatusCode = 200 };

    public static ApiResponse<T> CreatedResponse(T data, string? message = null)
        => new() { Success = true, Data = data, Message = message, StatusCode = 201 };

    public static ApiResponse<T> ErrorResponse(string message, int statusCode = 500)
        => new() { Success = false, Message = message, StatusCode = statusCode };

    public static ApiResponse<T> NotFoundResponse(string message = "Resource not found")
        => new() { Success = false, Message = message, StatusCode = 404 };

    public static ApiResponse<T> UnauthorizedResponse(string message = "Unauthorized")
        => new() { Success = false, Message = message, StatusCode = 401 };

    public static ApiResponse<T> ForbiddenResponse(string message = "Forbidden")
        => new() { Success = false, Message = message, StatusCode = 403 };

    public static ApiResponse<T> ValidationErrorResponse(Dictionary<string, string[]> errors, string? message = "Validation failed")
        => new() { Success = false, Message = message, Errors = errors, StatusCode = 422 };
}
