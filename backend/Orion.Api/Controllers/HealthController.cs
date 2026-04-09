using Microsoft.AspNetCore.Mvc;
using Orion.Core.DTOs;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;

namespace Orion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IHealthService _healthService;

    public HealthController(IHealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet]
    public IActionResult GetHealth()
    {
        var response = _healthService.GetHealthStatus();
        return StatusCode(response.StatusCode, response);
    }
}
