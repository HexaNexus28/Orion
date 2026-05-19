using Microsoft.AspNetCore.Mvc;
using Orion.Business.Daemon;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DaemonController : ControllerBase
{
    private readonly IDaemonClient _daemonClient;
    private readonly DaemonActionValidator _validator;
    private readonly IToolRegistry _toolRegistry;

    public DaemonController(IDaemonClient daemonClient, DaemonActionValidator validator, IToolRegistry toolRegistry)
    {
        _daemonClient = daemonClient;
        _validator = validator;
        _toolRegistry = toolRegistry;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var response = new
        {
            connected = _daemonClient.IsConnected,
            machineName = _daemonClient.MachineName
        };
        return Ok(ApiResponse<object>.SuccessResponse(response));
    }

    [HttpPost("action")]
    public async Task<IActionResult> ExecuteAction([FromBody] DaemonActionRequest action, CancellationToken ct)
    {
        try
        {
            _validator.ValidateOrThrow(action);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message, 400));
        }

        var result = await _daemonClient.SendActionAsync(action, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("tools")]
    public IActionResult GetAvailableTools()
    {
        var tools = _toolRegistry.GetAllTools()
            .Select(t => new { name = t.Name, description = t.Description })
            .OrderBy(t => t.name);

        return Ok(ApiResponse<object>.SuccessResponse(tools));
    }
}
