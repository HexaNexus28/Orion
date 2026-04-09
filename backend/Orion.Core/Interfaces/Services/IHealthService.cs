using Orion.Core.DTOs;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

public interface IHealthService
{
    ApiResponse<HealthCheckDto> GetHealthStatus();
}
