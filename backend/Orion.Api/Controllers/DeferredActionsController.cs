using Microsoft.AspNetCore.Mvc;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;

namespace Orion.Api.Controllers;

/// <summary>
/// La file d'actions différées, vue depuis l'UI.
///
/// Elle DOIT être visible et annulable : une file invisible est une promesse qu'on ne peut pas
/// reprendre, et ORION finirait par exécuter au réveil des choses que l'utilisateur a oubliées.
/// </summary>
[ApiController]
[Route("api/deferred-actions")]
public class DeferredActionsController : ControllerBase
{
    private readonly IDeferredActionService _file;

    public DeferredActionsController(IDeferredActionService file)
    {
        _file = file;
    }

    /// <summary>Ce qui attend, et ce qui a été fait récemment.</summary>
    [HttpGet]
    public async Task<IActionResult> GetQueue(CancellationToken ct)
    {
        var resultat = await _file.GetQueueAsync(ct);
        return StatusCode(resultat.StatusCode, resultat);
    }

    /// <summary>
    /// Confirme une action destructive que le drain a mise en attente. C'est ici que se
    /// referme la règle « elles se redemandent, elles ne se rejouent pas ».
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        var resultat = await _file.ConfirmAsync(id, ct);
        return StatusCode(resultat.StatusCode, resultat);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var resultat = await _file.CancelAsync(id, ct);
        return StatusCode(resultat.StatusCode, resultat);
    }
}
