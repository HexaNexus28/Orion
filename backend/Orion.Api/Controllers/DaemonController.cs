using Microsoft.AspNetCore.Mvc;
using Orion.Business.Daemon;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;

namespace Orion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DaemonController : ControllerBase
{
    private readonly IDaemonClient _daemonClient;
    private readonly DaemonActionValidator _validator;

    public DaemonController(IDaemonClient daemonClient, DaemonActionValidator validator)
    {
        _daemonClient = daemonClient;
        _validator = validator;
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
        var tools = new[]
        {
            new { name = "open_app", description = "Open an application", parameters = new[] { "application" } },
            new { name = "open_file", description = "Open a file in editor", parameters = new[] { "path", "editor" } },
            new { name = "run_script", description = "Run PowerShell script", parameters = new[] { "script", "workingDir" } },
            new { name = "open_url", description = "Open URL in browser", parameters = new[] { "url" } },
            new { name = "system_status", description = "Get system info", parameters = Array.Empty<string>() },
            new { name = "read_file", description = "Read file contents", parameters = new[] { "path", "maxLines" } },
            new { name = "write_file", description = "Write to file", parameters = new[] { "path", "content" } },
            new { name = "git_status", description = "Get git status", parameters = new[] { "path" } },
            new { name = "git_commit", description = "Commit changes", parameters = new[] { "path", "message" } },
        };

        return Ok(ApiResponse<object>.SuccessResponse(tools));
    }
}
