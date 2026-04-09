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
            ["return_html"] = new JsonObject { ["type"] = "boolean", ["default"] = false }
        },
        ["required"] = new JsonArray { "url" }
    };

    public WebBrowseTool(ILogger<WebBrowseTool> logger)
    {
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct)
    {
        var url = input["url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(url))
        {
            return ApiResponse<ToolResult>.ErrorResponse("URL parameter required", 400);
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
            await page.GotoAsync(url, new() { Timeout = 30000, WaitUntil = WaitUntilState.NetworkIdle });

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
                await page.GotoAsync(value, new() { Timeout = 30000 });
                break;
        }
    }
}
