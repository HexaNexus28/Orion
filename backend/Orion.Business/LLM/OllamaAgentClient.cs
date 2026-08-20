using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
/// Transport Ollama — endpoint NATIF /api/chat.
///
/// Pourquoi pas /v1/chat/completions (OpenAI-compatible) : mesuré le 2026-08-20, le shim
/// OpenAI d'Ollama PERD les tool calls en streaming — ils fuient dans "content" sous forme
/// de JSON malformé. L'endpoint natif les renvoie correctement structurés.
/// Ce client est donc volontairement spécifique à Ollama ; NVIDIA NIM aura le sien.
/// </summary>
public class OllamaAgentClient : ILLMAgentClient
{
    /// <summary>Nom du client HTTP typé configuré dans Program.cs.</summary>
    public const string HttpClientName = "OllamaAgent";

    private readonly IHttpClientFactory _httpFactory;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaAgentClient> _logger;

    // Singleton : le modèle qui répond réellement est résolu UNE fois par la sonde de
    // démarrage, pas re-testé (et re-refusé) à chaque tour.
    private volatile string _activeModel;
    private string? _primaryFailure;

    public OllamaAgentClient(
        IHttpClientFactory httpFactory,
        IOptions<OllamaOptions> options,
        ILogger<OllamaAgentClient> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
        _activeModel = _options.Model;
    }

    private HttpClient CreateClient() => _httpFactory.CreateClient(HttpClientName);

    public LLMProvider Provider => LLMProvider.Ollama;

    public string ModelId => _activeModel;

    /// <summary>
    /// On APPELLE le modèle — lister ne prouve rien : un modèle listé par `ollama list` peut être
    /// retiré ou verrouillé par abonnement (docs/jarvis-gap-analysis.md §1.10).
    /// </summary>
    public async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        foreach (var model in Candidates())
        {
            var (ok, detail) = await TryPingAsync(model, ct);
            if (ok)
            {
                if (!string.Equals(model, _options.Model, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError(
                        "[Ollama] MODELE PRINCIPAL INACCESSIBLE — '{Primary}' rejete ({Detail}). "
                        + "ORION tourne sur le repli '{Fallback}' : capacites de raisonnement REDUITES.",
                        _options.Model, _primaryFailure ?? "?", model);
                }
                _activeModel = model;
                return true;
            }

            _primaryFailure ??= detail;
            _logger.LogError("[Ollama] Modele '{Model}' inutilisable : {Detail}", model, detail);
        }

        return false;
    }

    private IEnumerable<string> Candidates()
    {
        yield return _options.Model;
        if (!string.Equals(_options.FallbackModel, _options.Model, StringComparison.OrdinalIgnoreCase))
            yield return _options.FallbackModel;
    }

    private async Task<(bool ok, string detail)> TryPingAsync(string model, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                model,
                stream = false,
                messages = new[] { new { role = "user", content = "ping" } },
                options = new { num_predict = 1, num_ctx = _options.NumCtx },
                keep_alive = _options.KeepAlive
            };

            using var http = CreateClient();
            using var response = await http.PostAsJsonAsync("/api/chat", payload, ct);
            if (response.IsSuccessStatusCode) return (true, "ok");

            var body = await response.Content.ReadAsStringAsync(ct);
            return (false, $"HTTP {(int)response.StatusCode} — {Trim(body, 200)}");
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
            ["options"] = new { temperature = request.Temperature ?? 0.7, num_ctx = _options.NumCtx },
            ["keep_alive"] = _options.KeepAlive
        };

        // LA correction du point mort : les tools sont enfin serialises dans le payload de
        // STREAMING. Avant, ce champ n'existait que sur le chemin non-streame — le modele
        // n'apprenait donc jamais qu'il avait des outils (docs/jarvis-gap-analysis.md §1.3).
        if (request.Tools is { Count: > 0 })
        {
            payload["tools"] = request.Tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.Parameters }
            }).ToList();
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(payload)
        };

        // ResponseHeadersRead est INDISPENSABLE : sans lui HttpClient bufferise toute la reponse
        // avant de rendre la main, et le "streaming token par token" arrive d'un seul bloc a la
        // fin. C'etait le cas de l'ancien client, qui utilisait PostAsJsonAsync.
        using var http = CreateClient();
        using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Ollama a refuse le modele '{model}' — HTTP {(int)response.StatusCode} : {Trim(body, 300)}");
        }

        var content = new StringBuilder();
        var toolCalls = new List<LLMToolCall>();
        int? tokens = null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            OllamaStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("[Ollama] Chunk illisible ignore : {Error}", ex.Message);
                continue;
            }

            if (chunk is null) continue;

            var text = chunk.Message?.Content;
            if (!string.IsNullOrEmpty(text))
            {
                content.Append(text);
                await onToken(text);
            }

            if (chunk.Message?.ToolCalls is { Count: > 0 } calls)
            {
                foreach (var call in calls)
                {
                    toolCalls.Add(new LLMToolCall
                    {
                        Id = string.IsNullOrWhiteSpace(call.Id) ? NewCallId() : call.Id,
                        Name = call.Function?.Name ?? string.Empty,
                        ArgumentsJson = RawArguments(call.Function)
                    });
                }
            }

            if (chunk.Done) tokens = chunk.EvalCount;
        }

        _logger.LogInformation(
            "[Ollama] Tour termine — modele {Model}, {Chars} caracteres, {Tools} appel(s) d'outil",
            model, content.Length, toolCalls.Count);

        return new LLMTurn
        {
            Content = content.ToString(),
            ToolCalls = toolCalls,
            Model = model,
            TokensUsed = tokens
        };
    }

    private static string NewCallId() => "call_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Ollama natif renvoie 'arguments' en OBJET JSON (OpenAI le renvoie en chaine).</summary>
    private static string RawArguments(OllamaToolCallFunction? function)
    {
        if (function is null) return "{}";

        return function.Arguments.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => "{}",
            JsonValueKind.String => function.Arguments.GetString() ?? "{}",
            _ => function.Arguments.GetRawText()
        };
    }

    private static List<object> BuildMessages(LLMRequest request)
    {
        var messages = new List<object>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });

        foreach (var m in request.Messages)
        {
            if (m.ToolCalls is { Count: > 0 })
            {
                // Le message assistant doit reporter ses tool_calls, sinon le modele ne relie pas
                // les resultats qui suivent aux appels qu'il a demandes.
                messages.Add(new
                {
                    role = m.Role,
                    content = m.Content,
                    tool_calls = m.ToolCalls.Select(tc => new
                    {
                        id = tc.Id,
                        function = new
                        {
                            name = tc.Name,
                            arguments = ParseArguments(tc.ArgumentsJson)
                        }
                    }).ToList()
                });
            }
            else if (string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(new { role = "tool", content = m.Content, tool_name = m.ToolName ?? string.Empty });
            }
            else
            {
                messages.Add(new { role = m.Role, content = m.Content });
            }
        }

        return messages;
    }

    private static JsonElement ParseArguments(string argumentsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        }
        catch (JsonException)
        {
            return JsonSerializer.Deserialize<JsonElement>("{}");
        }
    }

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";
}
