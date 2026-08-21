using System.Text.Json.Nodes;
using Orion.Core.DTOs.Internal.Tools;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Tools;

/// <summary>
/// Point d'application UNIQUE de l'exécution d'outil.
///
/// Avant lui, deux appelants faisaient `GetTool(name)` puis `ExecuteAsync` chacun de leur côté
/// (la boucle agent et l'API outils), et le garde « daemon absent » était recopié dans les
/// treize outils système. Une règle recopiée n'est pas une règle : c'est treize endroits où
/// l'oublier. Tout ce qui décide SI un outil s'exécute vit ici, et nulle part ailleurs.
/// </summary>
public interface IToolInvoker
{
    Task<ApiResponse<ToolResult>> InvokeAsync(
        string toolName,
        JsonObject input,
        ToolInvocationContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Exécute sans jamais différer, quel que soit l'état du daemon. Réservé au drain de la
    /// file : à ce moment le daemon vient de revenir, et re-différer une action ferait boucler.
    /// </summary>
    Task<ApiResponse<ToolResult>> InvokeNowAsync(
        string toolName,
        JsonObject input,
        CancellationToken ct = default);
}
