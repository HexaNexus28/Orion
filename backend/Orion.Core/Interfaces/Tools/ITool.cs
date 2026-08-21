using System.Text.Json.Nodes;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Tools;

public interface ITool
{
    string Name { get; }           // snake_case: "get_shiftstar_stats"
    string Description { get; }   // For the LLM
    JsonObject InputSchema { get; }

    /// <summary>
    /// L'outil passe par le daemon, donc il exige que le PC de l'utilisateur soit joignable.
    /// Le prompt s'en sert pour dire la vérité au modèle quand le daemon est hors ligne, au lieu
    /// de le laisser appeler un outil condamné à échouer.
    /// </summary>
    bool RequiresDaemon => false;

    /// <summary>
    /// L'outil écrit, supprime ou exécute quelque chose — son effet n'est pas annulable d'un clic.
    /// Le prompt impose une demande explicite avant de le déclencher.
    /// </summary>
    bool IsDestructive => false;

    /// <summary>
    /// L'outil garde un sens exécuté PLUS TARD, quand le PC se rallume.
    ///
    /// C'est l'utilité différée qui décide, pas la disponibilité. « Ouvre VS Code » ou
    /// « commit le travail » attendent très bien jusqu'au matin. « Qu'est-ce qu'il y a dans ce
    /// dossier ? » ne vaut plus rien demain : une réponse en retard de douze heures n'est pas
    /// une réponse, et la mettre en file ne ferait qu'encombrer la file.
    /// </summary>
    bool IsDeferrable => false;

    Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default);
}
