using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.Internet;

public class ScreenshotTool : ITool
{
    private readonly UrlScope _perimetre;
    private readonly ILogger<ScreenshotTool> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public string Name => "screenshot_page";
    public string Description => "Capture une page web → image base64 pour ORION";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["url"] = new JsonObject { ["type"] = "string", ["description"] = "URL à capturer" },
            ["full_page"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "Capturer la page entiere en defilant, au lieu de la seule zone visible",
                ["default"] = false
            },
            ["width"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Largeur de la fenetre en pixels",
                ["default"] = 1280
            },
            ["height"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Hauteur de la fenetre en pixels",
                ["default"] = 800
            }
        },
        ["required"] = new JsonArray { "url" }
    };

    public ScreenshotTool(UrlScope perimetre, ILogger<ScreenshotTool> logger)
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

        var fullPage = input["full_page"]?.GetValue<bool>() ?? false;
        var width = input["width"]?.GetValue<int>() ?? 1280;
        var height = input["height"]?.GetValue<int>() ?? 800;

        // Le contrôle d'avant était une liste de sous-chaînes — « banking », « secure »,
        // « login », « auth », « account » — cherchées dans l'URL en minuscules. Il refusait
        // une page Wikipédia dont le titre contient « Login », et laissait passer
        // http://169.254.169.254/ : bruyant sur le légitime, aveugle sur la menace.
        //
        // C'était aussi un TROISIÈME contrôle d'URL, plus faible que les deux autres. Il vit
        // désormais à un seul endroit, comme le reste (constat E2).
        var (cible, raison) = await _perimetre.VerifierAsync(url, ct);
        if (cible is null)
        {
            _logger.LogWarning("[screenshot_page] URL refusée : {Raison}", raison);
            return ApiResponse<ToolResult>.ForbiddenResponse(raison);
        }

        try
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new()
            {
                Headless = true
            });

            var page = await _browser.NewPageAsync(new()
            {
                ViewportSize = new ViewportSize { Width = width, Height = height }
            });

            await page.GotoAsync(cible.ToString(), new() { Timeout = 30000, WaitUntil = WaitUntilState.NetworkIdle });

            // Take screenshot
            var screenshotBytes = await page.ScreenshotAsync(new()
            {
                FullPage = fullPage,
                Type = ScreenshotType.Png
            });

            var base64Image = Convert.ToBase64String(screenshotBytes);
            var result = new ScreenshotResultDto
            {
                Url = url,
                Base64Image = base64Image,
                Width = width,
                Height = fullPage ? await GetScrollHeightAsync(page) : height
            };

            var toolResult = new ToolResult
            {
                Success = true,
                Data = JsonSerializer.SerializeToNode(result)
            };

            return ApiResponse<ToolResult>.SuccessResponse(toolResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Screenshot failed for URL: {Url}", url);
            return ApiResponse<ToolResult>.ErrorResponse($"Screenshot failed: {ex.Message}");
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

    private async Task<int> GetScrollHeightAsync(IPage page)
    {
        var height = await page.EvaluateAsync<int>("() => document.body.scrollHeight");
        return height;
    }
}
