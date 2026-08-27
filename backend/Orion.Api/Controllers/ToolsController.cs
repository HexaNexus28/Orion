using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orion.Api.Authentication;
using Orion.Api.Services;
using Orion.Core.DTOs.Internal.Tools;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Tools;

namespace Orion.Api.Controllers;

/// <summary>
/// Exécution d'un outil demandée par l'INTERFACE, et non par le modèle.
///
/// POURQUOI CE CONTRÔLEUR EXISTE. Les cartes du HUD proposent des gestes — « je commit ? »,
/// « je relance les tests ? ». Sans ce point d'entrée, l'utilisateur devrait formuler à voix
/// haute une demande que l'écran affiche déjà : un bouton qui ne fait que suggérer une phrase
/// n'est pas une action.
///
/// CE QUE ÇA N'ÉLARGIT PAS. Le modèle peut déjà appeler n'importe quel outil ; permettre à
/// l'interface de le faire n'ouvre aucune porte nouvelle. Quelqu'un qui détiendrait le jeton
/// pourrait de toute façon simplement DEMANDER à ORION de lancer l'outil.
///
/// CE QUI PROTÈGE. Le rôle « owner » est exigé par la politique par défaut, et l'appel passe par
/// <c>IToolInvoker</c> — donc par le garde-fou : un outil irréversible n'est pas exécuté, il est
/// mis en attente de confirmation. Un bouton « commit » se retrouve dans la file exactement comme
/// si le modèle l'avait demandé. Aucun second mécanisme à sécuriser.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ToolsController : ControllerBase
{
    private readonly IToolInvoker _invoker;
    private readonly IToolRegistry _registry;
    private readonly SseClientRegistry _sse;
    private readonly ILogger<ToolsController> _logger;

    public ToolsController(
        IToolInvoker invoker,
        IToolRegistry registry,
        SseClientRegistry sse,
        ILogger<ToolsController> logger)
    {
        _invoker = invoker;
        _registry = registry;
        _sse = sse;
        _logger = logger;
    }

    /// <summary>
    /// Liste ce qu'ORION sait faire. Sert à l'interface pour n'afficher que des gestes réellement
    /// disponibles — proposer un bouton dont l'outil n'existe plus produirait un échec côté
    /// utilisateur pour une erreur côté serveur.
    /// </summary>
    [Authorize(Policy = OrionAuth.OwnerPolicy)]
    [HttpGet]
    public IActionResult List()
    {
        var tools = _registry.GetAllTools().Select(t => new
        {
            name = t.Name,
            description = t.Description,
            requiresDaemon = t.RequiresDaemon,
            isDestructive = t.IsDestructive,
        });

        return Ok(ApiResponse<object>.SuccessResponse(tools));
    }

    /// <summary>
    /// Exécute un outil au nom de l'utilisateur.
    ///
    /// L'origine est marquée « hud » et non « chat » : quand une action irréversible part en file
    /// d'attente, ORION doit pouvoir dire d'où elle vient. « Tu as demandé un commit depuis le
    /// tableau de bord » et « tu me l'as demandé de vive voix » ne se rappellent pas pareil.
    /// </summary>
    [Authorize(Policy = OrionAuth.OwnerPolicy)]
    [HttpPost("{name}")]
    public async Task<IActionResult> Invoke(
        string name,
        [FromBody] JsonObject? arguments,
        CancellationToken ct)
    {
        if (_registry.GetTool(name) is null)
        {
            _logger.LogWarning("[Tools] Outil inconnu demande par l'interface : {Name}", name);
            return NotFound(ApiResponse<object>.ErrorResponse($"Outil '{name}' introuvable", 404));
        }

        _logger.LogInformation("[Tools] {Name} demande depuis le HUD", name);

        var context = new ToolInvocationContext(null, "hud", $"Action « {name} » depuis le HUD");
        var result = await _invoker.InvokeAsync(name, arguments ?? new JsonObject(), context, ct);

        // La carte produite est DIFFUSEE, pas seulement renvoyee a l appelant.
        //
        // Sans cela, « Rafraichir » executerait l outil et l ecran ne bougerait pas : un bouton
        // qui ne change rien est un faux succes. La diffusion emprunte le meme flux que les
        // panneaux permanents, donc la carte se met a jour PAR SON IDENTIFIANT — et sur tous les
        // ecrans ouverts, pas seulement celui qui a clique.
        if (result.Data?.Card is { } card)
            await _sse.BroadcastAsync("card", card);

        return StatusCode(result.StatusCode, result);
    }
}
