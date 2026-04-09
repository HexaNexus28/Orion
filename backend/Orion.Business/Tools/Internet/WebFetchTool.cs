using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.Internet;

public class WebFetchTool : ITool
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebFetchTool> _logger;

    public string Name => "web_fetch";
    public string Description => "Récupère le contenu texte d'une URL (article, doc, page)";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["url"] = new JsonObject { ["type"] = "string", ["description"] = "URL à récupérer" },
            ["max_length"] = new JsonObject { ["type"] = "integer", ["description"] = "Longueur max du contenu", ["default"] = 5000 }
        },
        ["required"] = new JsonArray { "url" }
    };

    public WebFetchTool(HttpClient httpClient, ILogger<WebFetchTool> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct)
    {
        var url = input["url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(url))
        {
            return ApiResponse<ToolResult>.ErrorResponse("URL parameter required", 400);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return ApiResponse<ToolResult>.ErrorResponse("Invalid URL format", 400);
        }

        var maxLength = input["max_length"]?.GetValue<int>() ?? 5000;

        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; ORION/1.0)");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            var response = await _httpClient.GetAsync(uri, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);
            var result = ExtractContent(html, uri.ToString(), maxLength);

            var toolResult = new ToolResult
            {
                Success = true,
                Data = JsonSerializer.SerializeToNode(result)
            };

            return ApiResponse<ToolResult>.SuccessResponse(toolResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web fetch failed for URL: {Url}", url);
            return ApiResponse<ToolResult>.ErrorResponse($"Fetch failed: {ex.Message}");
        }
    }

    private WebFetchResultDto ExtractContent(string html, string url, int maxLength)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove script and style elements
        doc.DocumentNode.Descendants()
            .Where(n => n.Name is "script" or "style" or "nav" or "footer" or "header" or "aside")
            .ToList()
            .ForEach(n => n.Remove());

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "No title";

        // Try to get main content
        var contentNode = doc.DocumentNode.SelectSingleNode("//article") 
            ?? doc.DocumentNode.SelectSingleNode("//main")
            ?? doc.DocumentNode.SelectSingleNode("//div[@role='main']")
            ?? doc.DocumentNode.SelectSingleNode("//body");

        var content = contentNode?.InnerText ?? "";
        
        // Clean up whitespace
        var sb = new StringBuilder();
        var lines = content.Split('\n', '\r');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && trimmed.Length > 2)
            {
                sb.Append(trimmed).Append(' ');
            }
        }

        content = sb.ToString();
        
        // Truncate if too long
        if (content.Length > maxLength)
        {
            content = content[..maxLength] + "... [truncated]";
        }

        return new WebFetchResultDto
        {
            Url = url,
            Title = title,
            Content = content,
            WordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
        };
    }
}
