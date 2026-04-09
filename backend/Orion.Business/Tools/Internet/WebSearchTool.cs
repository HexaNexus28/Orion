using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.Internet;

public class WebSearchTool : ITool
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebSearchTool> _logger;
    private readonly InternetOptions _options;

    public string Name => "web_search";
    public string Description => "Recherche web via Brave Search API ou SerpAPI";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Termes de recherche" },
            ["count"] = new JsonObject { ["type"] = "integer", ["description"] = "Nombre de résultats (max 10)", ["default"] = 5 }
        },
        ["required"] = new JsonArray { "query" }
    };

    public WebSearchTool(HttpClient httpClient, ILogger<WebSearchTool> logger, IOptions<InternetOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct)
    {
        var query = input["query"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return ApiResponse<ToolResult>.ErrorResponse("Query parameter required", 400);
        }

        var count = Math.Min(input["count"]?.GetValue<int>() ?? 5, 10);

        try
        {
            var results = _options.SearchApiProvider.ToLower() switch
            {
                "brave" => await SearchBraveAsync(query, count, ct),
                "serpapi" => await SearchSerpApiAsync(query, count, ct),
                _ => await SearchBraveAsync(query, count, ct)
            };

            var toolResult = new ToolResult
            {
                Success = true,
                Data = JsonSerializer.SerializeToNode(results)
            };

            return ApiResponse<ToolResult>.SuccessResponse(toolResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web search failed for query: {Query}", query);
            return ApiResponse<ToolResult>.ErrorResponse($"Search failed: {ex.Message}");
        }
    }

    private async Task<List<WebSearchResultDto>> SearchBraveAsync(string query, int count, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.BraveApiKey))
        {
            throw new InvalidOperationException("Brave API key not configured");
        }

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("X-Subscription-Token", _options.BraveApiKey);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={count}";
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);

        var results = new List<WebSearchResultDto>();
        if (doc.RootElement.TryGetProperty("web", out var web) && 
            web.TryGetProperty("results", out var items))
        {
            foreach (var item in items.EnumerateArray().Take(count))
            {
                results.Add(new WebSearchResultDto
                {
                    Title = item.GetProperty("title").GetString() ?? "",
                    Url = item.GetProperty("url").GetString() ?? "",
                    Snippet = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                    Source = item.TryGetProperty("profile", out var prof) && prof.TryGetProperty("name", out var name) 
                        ? name.GetString() : null
                });
            }
        }

        return results;
    }

    private async Task<List<WebSearchResultDto>> SearchSerpApiAsync(string query, int count, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.SerpApiKey))
        {
            throw new InvalidOperationException("SerpAPI key not configured");
        }

        var url = $"https://serpapi.com/search.json?q={Uri.EscapeDataString(query)}&num={count}&api_key={_options.SerpApiKey}";
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);

        var results = new List<WebSearchResultDto>();
        if (doc.RootElement.TryGetProperty("organic_results", out var items))
        {
            foreach (var item in items.EnumerateArray().Take(count))
            {
                results.Add(new WebSearchResultDto
                {
                    Title = item.GetProperty("title").GetString() ?? "",
                    Url = item.GetProperty("link").GetString() ?? "",
                    Snippet = item.TryGetProperty("snippet", out var snip) ? snip.GetString() ?? "" : "",
                    Source = item.TryGetProperty("source", out var src) ? src.GetString() : null
                });
            }
        }

        return results;
    }
}
