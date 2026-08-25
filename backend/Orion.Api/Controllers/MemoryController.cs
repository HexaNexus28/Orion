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
    private readonly IMemoryRevectorizer _revectorizer;
    private readonly ILogger<MemoryController> _logger;

    public MemoryController(IMemoryService memoryService, IMemoryRevectorizer revectorizer, ILogger<MemoryController> logger)
    {
        _memoryService = memoryService;
        _revectorizer = revectorizer;
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

    /// <summary>
    /// Recalcule les vecteurs avec le modele d'embedding COURANT.
    ///
    /// A lancer apres tout changement de fournisseur : deux modeles = deux espaces vectoriels
    /// incomparables, et les melanger renvoie des resultats absurdes sans lever d'erreur.
    /// Le rapport indique combien de souvenirs etaient concernes — la reponse a « combien y
    /// en avait-il ? », qu'on ne peut pas connaitre autrement.
    ///
    /// `maxRows` permet un premier essai borne avant de lancer la totalite.
    /// </summary>
    [HttpPost("revectorize")]
    [ProducesResponseType(typeof(ApiResponse<RevectorizeReport>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Revectorize([FromQuery] int? maxRows, CancellationToken ct)
    {
        _logger.LogInformation("[Memory] Revectorisation demandee (maxRows={Max})", maxRows);
        var response = await _revectorizer.RunAsync(maxRows, ct);
        return StatusCode(response.StatusCode, response);
    }
}
