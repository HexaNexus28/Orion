using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Frontière métier au-dessus du transport LLM.
/// Le métier interroge CE service, jamais un client LLM directement.
/// </summary>
public interface ILLMService
{
    ApiResponse<LLMStatusDto> GetStatus();
}
