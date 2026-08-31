using System.Text.Json;
using System.Text.Json.Nodes;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

public class GitStatusTool : ITool
{
    private readonly IDaemonClient _daemon;

    public GitStatusTool(IDaemonClient daemon)
    {
        _daemon = daemon;
    }

    public string Name => "git_status";
    public string Description => "Retourne le statut git d'un dépôt (branche, fichiers modifiés)";

    public bool RequiresDaemon => true;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Chemin du dépôt git (optionnel, défaut: répertoire courant)"
            }
        }
    };

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {

        var path = input["path"]?.GetValue<string>() ?? ".";

        var request = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "git_status",
            Payload = new { path }
        };

        var result = await _daemon.SendActionAsync(request, ct);

        if (!result.Success)
        {
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);
        }

        return ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(
            JsonSerializer.Serialize(result.Data?.Data),
            Name));
    }

    /// <summary>
    /// Carte « depot ». Identifiant porte le NOM DU DEPOT (git.ShiftStar) et non l outil :
    /// interroger deux depots doit produire deux cartes, pas ecraser la premiere.
    /// </summary>
    public HudCard? BuildCard(ToolResult result)
    {
        if (result.Data is not string json) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;

            var chemin = r.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
            // Séparateurs EN DUR, jamais ceux de la plateforme.
            //
            // `chemin` vient du daemon WINDOWS (« C:\Projets\ShiftCore »), mais ce code tourne
            // dans le backend, c'est-à-dire un conteneur LINUX. Là, `Path.DirectorySeparatorChar`
            // vaut « / » : le découpage ne coupait rien et l'identifiant de carte devenait
            // « git.C:\Projets\ShiftCore » au lieu de « git.ShiftCore ».
            //
            // Deux dépôts continuaient bien à donner deux cartes, donc rien ne se voyait — mais
            // l'identifiant n'était plus stable ni lisible. Le test le disait depuis toujours ;
            // il passait sur la machine Windows du développeur et n'avait jamais été exécuté
            // ailleurs, faute de CI.
            var depot = chemin.TrimEnd('\\', '/')
                              .Split('\\', '/')
                              .LastOrDefault() ?? "depot";

            var branche = r.TryGetProperty("branch", out var b) ? b.GetString() : null;

            var changements = new List<string>();
            if (r.TryGetProperty("changes", out var c) && c.ValueKind == JsonValueKind.Array)
            {
                changements.AddRange(c.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
            }

            // Plafonne a 6 : au-dela la carte deviendrait un mur de texte, et le compte total
            // porte deja l information utile.
            var items = changements.Take(6)
                .Select(l => new HudCardItem { Label = l.Trim() })
                .ToList();
            if (changements.Count > 6)
                items.Add(new HudCardItem { Label = $"... et {changements.Count - 6} autres" });

            return new HudCard
            {
                Id = $"git.{depot}",
                Kind = HudCardKind.Status,
                Label = depot,
                Value = branche,
                Unit = changements.Count > 0 ? $"{changements.Count} modif." : "propre",
                State = changements.Count > 0 ? HudCardState.Warn : HudCardState.Ok,
                Items = items.Count > 0 ? items : null,

                // Le chemin vient de la carte elle-meme : sans lui, « rafraichir » relirait le
                // depot par defaut et afficherait l etat d un AUTRE projet sous le meme titre.
                Actions = new List<HudCardAction>
                {
                    new()
                    {
                        Label = "Rafraichir",
                        Tool = "git_status",
                        Arguments = JsonSerializer.Serialize(new { path = chemin }),
                    },
                }
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
