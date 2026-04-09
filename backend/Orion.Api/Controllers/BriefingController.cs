using Microsoft.AspNetCore.Mvc;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;

namespace Orion.Api.Controllers;

/// <summary>
/// BriefingController - Morning briefing & daily summaries
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BriefingController : ControllerBase
{
    private readonly IBriefingService _briefingService;
    private readonly ILogger<BriefingController> _logger;

    public BriefingController(IBriefingService briefingService, ILogger<BriefingController> logger)
    {
        _briefingService = briefingService;
        _logger = logger;
    }

    /// <summary>
    /// Get today's briefing
    /// </summary>
    [HttpGet("today")]
    [ProducesResponseType(typeof(ApiResponse<BriefingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetToday(CancellationToken ct)
    {
        var response = await _briefingService.GenerateTodayBriefingAsync(ct);
        return StatusCode(response.StatusCode, response);
    }

    /// <summary>
    /// Get briefing history
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(ApiResponse<List<BriefingDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var response = await _briefingService.GetBriefingHistoryAsync(days, ct);
        return StatusCode(response.StatusCode, response);
    }
}
