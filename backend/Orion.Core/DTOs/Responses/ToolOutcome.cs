namespace Orion.Core.DTOs.Responses;

/// <summary>
/// Ce qu un outil rend a la boucle agent : le JSON destine au MODELE, et la carte destinee a
/// l INTERFACE.
///
/// Les deux voyagent cote a cote et ne se melangent pas. Mettre la carte dans le JSON la ferait
/// lire par le modele — des jetons payes pour de la mise en forme, et un modele qui pourrait se
/// mettre a commenter l affichage au lieu de repondre.
/// </summary>
public record ToolOutcome(string Json, HudCard? Card = null);