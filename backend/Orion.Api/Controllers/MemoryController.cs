using Microsoft.AspNetCore.Mvc;
using Orion.Core.DTOs;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;

namespace Orion.Api.Controllers;

/// <summary>
/// MemoryController - RAG memory management
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MemoryController : ControllerBase
{
    private readonly IMemoryService _memoryService;
    private readonly ILogger<MemoryController> _logger;

    public MemoryController(IMemoryService memoryService, ILogger<MemoryController> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }

    /// <summary>
    /// Search memories by query (RAG)
    /// </summary>
    [HttpPost("search")]
    [ProducesResponseType(typeof(ApiResponse<List<MemoryVectorDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromBody] MemorySearchRequest request, CancellationToken ct)
    {
        var response = await _memoryService.SearchSimilarAsync(request.Query, request.Limit, ct);
        return StatusCode(response.StatusCode, response);
    }

    /// <summary>
    /// Get all memories
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<MemoryVectorDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var response = await _memoryService.GetAllMemoriesAsync(ct);
        return StatusCode(response.StatusCode, response);
    }

    /// <summary>
    /// Delete a memory by ID
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var response = await _memoryService.DeleteMemoryAsync(id.ToString(), ct);
        return StatusCode(response.StatusCode, response);
    }
}
