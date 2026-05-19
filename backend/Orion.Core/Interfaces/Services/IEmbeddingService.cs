using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

public interface IEmbeddingService
{
    Task<ApiResponse<float[]>> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}
