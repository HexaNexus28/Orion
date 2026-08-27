using System.Text.Json;
using System.Text.Json.Nodes;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.System;

/// <summary>
/// Ce sur quoi l'utilisateur travaille en ce moment.
///
/// `get_system_status` répond « quelle machine ». Celui-ci répond « quel travail » — et c'est ce
/// qui permet à ORION de proposer le geste suivant au lieu d'afficher un indicateur.
/// </summary>
public class GetWorkContextTool : ITool
{
    private readonly IDaemonClient _daemon;

    public GetWorkContextTool(IDaemonClient daemon) => _daemon = daemon;

    public string Name => "get_work_context";

    public string Description =>
        "Ce sur quoi l'utilisateur travaille maintenant : application au premier plan, fichier et "
        + "projet ouverts. À utiliser avant de proposer une action liée à son travail en cours.";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
    };

    public bool RequiresDaemon => true;

    /// <summary>
    /// Non différable : « sur quoi travaillait-il il y a douze heures » ne vaut rien. Une lecture
    /// de contexte périmée est pire qu'une absence de réponse — elle ferait proposer une action
    /// sur un fichier que l'utilisateur a quitté depuis longtemps.
    /// </summary>
    public bool IsDeferrable => false;

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        var request = new DaemonActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Action = "work_context",
            Payload = new { },
        };

        var result = await _daemon.SendActionAsync(request, ct);

        if (!result.Success)
            return ApiResponse<ToolResult>.ErrorResponse(result.Message ?? "Daemon error", result.StatusCode);

        return ApiResponse<ToolResult>.SuccessResponse(
            ToolResult.SuccessResult(JsonSerializer.Serialize(result.Data?.Data), Name));
    }

    /// <summary>
    /// Widget PERMANENT — le premier du HUD.
    ///
    /// L'analyse du titre vit ICI et non dans le daemon : chaque éditeur a son format et ils
    /// changent au fil des versions. Ajuster côté backend prend deux minutes de déploiement ;
    /// côté daemon il faudrait repasser sur la machine de l'utilisateur.
    /// </summary>
    public HudCard? BuildCard(ToolResult result)
    {
        if (result.Data is not string json) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var active = root.TryGetProperty("active", out var a) && a.GetBoolean();
            var application = root.TryGetProperty("application", out var app) ? app.GetString() : null;
            var title = root.TryGetProperty("windowTitle", out var t) ? t.GetString() : null;

            if (!active)
            {
                return new HudCard
                {
                    Id = "work.context",
                    Kind = HudCardKind.Status,
                    Lifetime = HudCardLifetime.Pinned,
                    Label = "Contexte",
                    Value = "session inactive",
                    State = HudCardState.Neutral,
                };
            }

            var file = FindFile(title);

            var items = new List<HudCardItem>();

            // Le titre complet est montre tel quel : il porte le projet, l onglet ou le dossier
            // selon l application, et lui coller une etiquette serait inventer.
            if (title is not null)
                items.Add(new HudCardItem { Label = title.Length > 60 ? title[..60] + "..." : title });

            if (application is not null) items.Add(new HudCardItem { Label = "Application", Value = application });

            return new HudCard
            {
                Id = "work.context",
                Kind = HudCardKind.Status,
                Lifetime = HudCardLifetime.Pinned,
                Label = "Contexte",

                // Le FICHIER en valeur principale : c'est l'unité de travail réelle, pas
                // l'application. « useVAD.ts » dit quelque chose, « Code » ne dit rien.
                Value = file ?? application ?? "inconnu",
                State = HudCardState.Ok,
                Items = items.Count > 0 ? items : null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Repere le FICHIER dans un titre de fenetre. Ne devine rien d autre.
    ///
    /// Le decoupage par POSITION ne peut pas fonctionner : chaque application a son ordre.
    /// Titres reels releves le 2026-08-27 :
    ///   Devin    « tiktok-workflow - Devin - .env.cloudflare »   projet - app - fichier
    ///   Notepad  « supabase-shiftstar-dev.env - Bloc-notes »     fichier - app
    ///   VS Code  « useVAD.ts - ShiftCore - Visual Studio Code »  fichier - projet - app
    ///
    /// Une premiere version prenait le premier segment comme fichier et l avant-dernier comme
    /// projet : sur Devin elle annoncait « fichier : tiktok-workflow, projet : Devin ». Un
    /// contexte FAUX est pire qu un contexte absent — il ferait proposer une action sur le
    /// mauvais fichier.
    ///
    /// On ne retient donc que ce qui est identifiable avec certitude : un segment portant une
    /// extension est un fichier. Le reste est montre BRUT, sans etiquette inventee.
    /// </summary>
    private static string? FindFile(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var segments = title.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var brut in segments)
        {
            // Une pastille precede le nom quand le fichier a des modifications non enregistrees.
            var segment = brut.TrimStart('●', '*', ' ');
            if (segment.Length == 0) continue;

            // Fichier CACHE : « .env », « .gitignore », « .env.cloudflare ». Leur suffixe n est
            // pas une extension courte — « cloudflare » fait dix caracteres — et la regle
            // ci-dessous les rejetait. Le cas s est presente des le premier test reel.
            if (segment[0] == '.' && segment.Length > 1) return segment;

            var point = segment.LastIndexOf('.');
            if (point <= 0 || point == segment.Length - 1) continue;

            var extension = segment[(point + 1)..];
            // Une extension plausible : courte et alphanumerique. « Bloc-notes » n en a pas,
            // « 29 pages de plus » non plus.
            if (extension.Length is >= 1 and <= 6 && extension.All(char.IsLetterOrDigit))
                return segment;
        }

        return null;
    }
}
