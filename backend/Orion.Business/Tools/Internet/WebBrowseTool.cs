using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.Internet;

public class WebBrowseTool : ITool
{
    private readonly ILogger<WebBrowseTool> _logger;
    private readonly UrlScope _perimetre;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public string Name => "web_browse";
    public string Description => "Navigation interactive avec Playwright (scroll, click, formulaires)";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["url"] = new JsonObject { ["type"] = "string", ["description"] = "URL de départ" },
            ["actions"] = new JsonObject 
            { 
                ["type"] = "array",
                ["description"] = "Actions à exécuter : { type: 'goto'|'click'|'fill'|'scroll'|'wait', selector?: string, value?: string }",
                ["items"] = new JsonObject { ["type"] = "object" }
            },
            ["return_html"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Renvoyer le HTML brut de la page en plus du texte extrait",
                ["default"] = false
            }
        },
        ["required"] = new JsonArray { "url" }
    };

    public WebBrowseTool(UrlScope perimetre, ILogger<WebBrowseTool> logger)
    {
        _perimetre = perimetre;
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct)
    {
        var url = input["url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(url))
        {
            return ApiResponse<ToolResult>.ErrorResponse("URL parameter required", 400);
        }

        var (depart, raison) = await _perimetre.VerifierAsync(url, ct);
        if (depart is null)
        {
            _logger.LogWarning("[web_browse] URL refusée : {Raison}", raison);
            return ApiResponse<ToolResult>.ErrorResponse(raison, 400);
        }

        var returnHtml = input["return_html"]?.GetValue<bool>() ?? false;
        var actionsArray = input["actions"]?.AsArray();

        try
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new()
            {
                Headless = true
            });

            var page = await _browser.NewPageAsync();

            // Vérifier l'URL de départ ne suffit pas : c'est le NAVIGATEUR qui suit les
            // redirections, et une page publique qui renvoie 302 vers 169.254.169.254 nous ferait
            // extraire le contenu de la destination. On filtre donc chaque NAVIGATION.
            //
            // Seulement les navigations : valider aussi les images et les feuilles de style
            // ajouterait une résolution DNS par ressource, pour un risque bien moindre — une
            // sous-ressource n'est pas relue et ne repart pas vers le modèle.
            await page.RouteAsync("**/*", async route =>
            {
                // PROPRIETE, pas methode : `IsNavigationRequest()` donne CS1955.
                if (!route.Request.IsNavigationRequest)
                {
                    await route.ContinueAsync();
                    return;
                }

                var (autorisee, motif) = await _perimetre.VerifierAsync(route.Request.Url);
                if (autorisee is null)
                {
                    _logger.LogWarning("[web_browse] Navigation bloquée vers {Url} : {Motif}",
                        route.Request.Url, motif);
                    await route.AbortAsync("blockedbyclient");
                    return;
                }

                await route.ContinueAsync();
            });

            await page.GotoAsync(depart.ToString(), new() { Timeout = 30000, WaitUntil = WaitUntilState.NetworkIdle });

            // Execute actions if provided
            if (actionsArray != null)
            {
                foreach (var action in actionsArray)
                {
                    if (action == null) continue;
                    
                    var type = action["type"]?.GetValue<string>() ?? "";
                    var selector = action["selector"]?.GetValue<string>();
                    var value = action["value"]?.GetValue<string>();
                    var delay = action["delay_ms"]?.GetValue<int>() ?? 1000;

                    await ExecuteBrowserActionAsync(page, type, selector, value, delay, ct);
                }
            }

            // Get result
            var title = await page.TitleAsync();
            var finalUrl = page.Url;
            var result = new Dictionary<string, object>
            {
                ["title"] = title,
                ["url"] = finalUrl
            };

            if (returnHtml)
            {
                var html = await page.ContentAsync();
                result["html"] = html[..Math.Min(html.Length, 10000)]; // Limit size
            }

            // Try to extract main text content
            var bodyText = await page.EvaluateAsync<string>("() => document.body.innerText");
            if (!string.IsNullOrEmpty(bodyText))
            {
                result["text"] = bodyText[..Math.Min(bodyText.Length, 5000)];
            }

            var toolResult = new ToolResult
            {
                Success = true,
                Data = JsonSerializer.SerializeToNode(result)
            };

            return ApiResponse<ToolResult>.SuccessResponse(toolResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web browse failed for URL: {Url}", url);
            return ApiResponse<ToolResult>.ErrorResponse($"Browse failed: {ex.Message}");
        }
        finally
        {
            if (_browser != null)
            {
                await _browser.CloseAsync();
            }
            _playwright?.Dispose();
        }
    }

    private async Task ExecuteBrowserActionAsync(IPage page, string type, string? selector, string? value, int delay, CancellationToken ct)
    {
        switch (type.ToLower())
        {
            case "click" when !string.IsNullOrEmpty(selector):
                await page.ClickAsync(selector);
                await Task.Delay(delay, ct);
                break;

            case "fill" when !string.IsNullOrEmpty(selector) && !string.IsNullOrEmpty(value):
                await page.FillAsync(selector, value);
                break;

            case "scroll":
                await page.EvaluateAsync("() => window.scrollBy(0, 800)");
                await Task.Delay(500, ct);
                break;

            case "wait":
                await Task.Delay(delay, ct);
                break;

            case "goto" when !string.IsNullOrEmpty(value):
                // L'action `goto` est une SECONDE porte d'entrée : le modèle y met une URL
                // arbitraire, distincte de celle qu'on a validée à l'ouverture. Le filtre de
                // navigation l'attraperait, mais un refus explicite dit POURQUOI.
                var (cible, motifRefus) = await _perimetre.VerifierAsync(value, ct);
                if (cible is null)
                    throw new InvalidOperationException($"Navigation refusée vers {value} — {motifRefus}");

                await page.GotoAsync(cible.ToString(), new() { Timeout = 30000 });
                break;
        }
    }
}
