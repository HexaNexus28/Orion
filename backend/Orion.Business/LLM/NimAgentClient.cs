using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Internal.LLM;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;

namespace Orion.Business.LLM;

/// <summary>
/// Transport NVIDIA NIM — endpoint OpenAI-compatible `/chat/completions`, adossé à vLLM.
///
/// Contrairement au shim OpenAI d'Ollama, celui-ci renvoie bien les tool calls EN STREAMING,
/// sous forme de deltas structurés (vérifié le 2026-08-20 sur la clé du projet).
///
/// Différence de format à ne pas rater : `function.arguments` arrive ici en **CHAÎNE JSON**
/// (`"{\"appName\":\"Notepad\"}"`), là où l'endpoint natif d'Ollama le donne en **OBJET**.
/// Les deux normalisent vers <see cref="LLMTurn"/>.
/// </summary>
public class NimAgentClient : ILLMAgentClient
{
    public const string HttpClientName = "NimAgent";

    private readonly IHttpClientFactory _httpFactory;
    private readonly NimOptions _options;
    private readonly ILogger<NimAgentClient> _logger;

    private volatile string _activeModel;
    private string? _primaryFailure;

    public NimAgentClient(
        IHttpClientFactory httpFactory,
        IOptions<NimOptions> options,
        ILogger<NimAgentClient> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
        _activeModel = _options.Model;
    }

    public LLMProvider Provider => LLMProvider.Nim;

    public string ModelId => _activeModel;

    private HttpClient CreateClient()
    {
        var client = _httpFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        return client;
    }

    /// <summary>
    /// Appelle réellement le modèle. Indispensable ici : NVIDIA retire des modèles sans retirer
    /// leur page du catalogue — `deepseek-ai/deepseek-v4-pro` répondait `410 Gone / end of life`
    /// le 2026-08-20 alors qu'il était toujours affiché comme disponible.
    /// </summary>
    public async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogWarning("[NIM] Aucune cle API configuree — fournisseur ignore");
            return false;
        }

        foreach (var model in Candidates())
        {
            var (ok, detail) = await TryPingAsync(model, ct);
            if (ok)
            {
                if (!string.Equals(model, _options.Model, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError(
                        "[NIM] MODELE PRINCIPAL INDISPONIBLE — '{Primary}' rejete ({Detail}). Repli sur '{Fallback}'.",
                        _options.Model, _primaryFailure ?? "?", model);
                }

                _activeModel = model;
                _logger.LogInformation("[NIM] Modele operationnel : {Model}", model);
                return true;
            }

            _primaryFailure ??= detail;
            _logger.LogError("[NIM] Modele '{Model}' inutilisable : {Detail}", model, detail);
        }

        return false;
    }

    private IEnumerable<string> Candidates()
    {
        yield return _options.Model;
        if (!string.IsNullOrWhiteSpace(_options.FallbackModel) &&
            !string.Equals(_options.FallbackModel, _options.Model, StringComparison.OrdinalIgnoreCase))
        {
            yield return _options.FallbackModel;
        }
    }

    private async Task<(bool ok, string detail)> TryPingAsync(string model, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                model,
                stream = false,
                max_tokens = 4,
                messages = new[] { new { role = "user", content = "ping" } }
            };

            using var http = CreateClient();
            using var response = await http.PostAsJsonAsync("chat/completions", payload, ct);
            if (response.IsSuccessStatusCode) return (true, "ok");

            var body = await response.Content.ReadAsStringAsync(ct);
            return (false, $"HTTP {(int)response.StatusCode} — {Trim(body, 220)}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public async Task<LLMTurn> StreamTurnAsync(
        LLMRequest request,
        Func<string, Task> onToken,
        CancellationToken ct = default)
    {
        var model = request.Model ?? _activeModel;

        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = BuildMessages(request),
            ["stream"] = true,
            ["temperature"] = request.Temperature ?? 0.7,
        };

        if (request.MaxTokens is > 0) payload["max_tokens"] = request.MaxTokens.Value;

        if (request.Tools is { Count: > 0 })
        {
            payload["tools"] = request.Tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.Parameters }
            }).ToList();
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var http = CreateClient();
        using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"NIM a refuse le modele '{model}' — HTTP {(int)response.StatusCode} : {Trim(body, 300)}");
        }

        var content = new StringBuilder();
        // Les fragments d'un meme appel d'outil sont repartis sur plusieurs chunks et se
        // recollent par `index` — c'est la convention OpenAI, pas une particularite de NIM.
        var toolCalls = new SortedDictionary<int, ToolCallAccumulator>();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line[5..].Trim();
            if (data.Length == 0 || data == "[DONE]") continue;

            OpenAiStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OpenAiStreamChunk>(data);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("[NIM] Chunk illisible ignore : {Error}", ex.Message);
                continue;
            }

            var delta = chunk?.Choices?.FirstOrDefault()?.Delta;
            if (delta is null) continue;

            if (!string.IsNullOrEmpty(delta.Content))
            {
                content.Append(delta.Content);
                await onToken(delta.Content);
            }

            if (delta.ToolCalls is null) continue;

            foreach (var fragment in delta.ToolCalls)
            {
                var index = fragment.Index ?? 0;
                if (!toolCalls.TryGetValue(index, out var accumulator))
                {
                    accumulator = new ToolCallAccumulator();
                    toolCalls[index] = accumulator;
                }

                if (!string.IsNullOrEmpty(fragment.Id)) accumulator.Id = fragment.Id;
                if (!string.IsNullOrEmpty(fragment.Function?.Name)) accumulator.Name = fragment.Function.Name;
                if (!string.IsNullOrEmpty(fragment.Function?.Arguments)) accumulator.Arguments.Append(fragment.Function.Arguments);
            }
        }

        var calls = toolCalls.Values
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new LLMToolCall
            {
                Id = string.IsNullOrWhiteSpace(a.Id) ? NewCallId() : a.Id,
                Name = a.Name,
                ArgumentsJson = a.Arguments.Length == 0 ? "{}" : a.Arguments.ToString()
            })
            .ToList();

        _logger.LogInformation(
            "[NIM] Tour termine — modele {Model}, {Chars} caracteres, {Tools} appel(s) d'outil",
            model, content.Length, calls.Count);

        return new LLMTurn
        {
            Content = content.ToString(),
            ToolCalls = calls,
            Model = model
        };
    }

    private sealed class ToolCallAccumulator
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public StringBuilder Arguments { get; } = new();
    }

    private static string NewCallId() => "call_" + Guid.NewGuid().ToString("N")[..8];

    private static List<object> BuildMessages(LLMRequest request)
    {
        var messages = new List<object>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });

        foreach (var m in request.Messages)
        {
            if (m.ToolCalls is { Count: > 0 })
            {
                messages.Add(new
                {
                    role = m.Role,
                    content = m.Content,
                    tool_calls = m.ToolCalls.Select(tc => new
                    {
                        id = tc.Id,
                        type = "function",
                        // Format OpenAI : les arguments sont une CHAINE, pas un objet.
                        function = new { name = tc.Name, arguments = tc.ArgumentsJson }
                    }).ToList()
                });
            }
            else if (string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(new { role = "tool", content = m.Content, tool_call_id = m.ToolCallId ?? string.Empty });
            }
            else
            {
                messages.Add(new { role = m.Role, content = m.Content });
            }
        }

        return messages;
    }

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";

    // ── DTOs de transport (format OpenAI) ───────────────────────────────────────

    private sealed class OpenAiStreamChunk
    {
        [JsonPropertyName("choices")]
        public List<OpenAiChoice>? Choices { get; set; }
    }

    private sealed class OpenAiChoice
    {
        [JsonPropertyName("delta")]
        public OpenAiDelta? Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private sealed class OpenAiDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("tool_calls")]
        public List<OpenAiToolCallFragment>? ToolCalls { get; set; }
    }

    private sealed class OpenAiToolCallFragment
    {
        [JsonPropertyName("index")]
        public int? Index { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("function")]
        public OpenAiFunctionFragment? Function { get; set; }
    }

    private sealed class OpenAiFunctionFragment
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; }
    }
}
