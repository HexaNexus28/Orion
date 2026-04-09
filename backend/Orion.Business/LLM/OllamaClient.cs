using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Internal.LLM;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;

namespace Orion.Business.LLM;

public class OllamaClient : ILLMClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaClient> _logger;
    private readonly OllamaOptions _options;

    public OllamaClient(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public LLMProvider Provider => LLMProvider.Ollama;

    public bool IsAvailable()
    {
        try
        {
            var response = _httpClient.GetAsync("/api/tags").Result;
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ApiResponse<LLMResponse>> CompleteAsync(LLMRequest request, CancellationToken ct = default)
    {
        var primaryModel = request.Model ?? _options.Model;
        var fallbackModel = _options.FallbackModel;

        // Try primary model first
        var result = await TryCompleteWithModelAsync(request, primaryModel, ct);

        // If primary fails due to memory or model not found, try fallback
        if (!result.Success && ShouldTryFallback(result.Message))
        {
            _logger.LogWarning("Primary model {Primary} failed, trying fallback {Fallback}", primaryModel, fallbackModel);
            result = await TryCompleteWithModelAsync(request, fallbackModel, ct);
            if (result.Success)
            {
                _logger.LogInformation("Fallback model {Fallback} succeeded", fallbackModel);
            }
        }

        return result;
    }

    private async Task<ApiResponse<LLMResponse>> TryCompleteWithModelAsync(LLMRequest request, string model, CancellationToken ct)
    {
        try
        {
            // Build message list: start with system prompt if present
            var messages = BuildMessageList(request);

            // Build tools array for Ollama if provided
            object? toolsPayload = null;
            if (request.Tools != null && request.Tools.Count > 0)
            {
                toolsPayload = request.Tools.Select(t => new
                {
                    type = "function",
                    function = new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = t.Parameters
                    }
                }).ToList();
            }

            var ollamaRequest = toolsPayload != null
                ? (object)new
                {
                    model = model,
                    messages = messages,
                    stream = false,
                    options = new { temperature = request.Temperature ?? 0.7 },
                    tools = toolsPayload
                }
                : new
                {
                    model = model,
                    messages = messages,
                    stream = false,
                    options = new { temperature = request.Temperature ?? 0.7 }
                };

            var response = await _httpClient.PostAsJsonAsync("/api/chat", ollamaRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Ollama API error for model {Model}: {StatusCode} - {Error}", model, response.StatusCode, error);
                return ApiResponse<LLMResponse>.ErrorResponse($"Ollama error ({model}): {error}", (int)response.StatusCode);
            }

            var rawJson = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("[Ollama] Raw JSON for {Model}: {Json}", model, rawJson.Substring(0, Math.Min(500, rawJson.Length)));

            var jsonResponse = JsonSerializer.Deserialize<OllamaResponse>(rawJson);

            _logger.LogInformation("[Ollama] Deserialized - Message null: {IsNull}, Content: '{Content}', DoneReason: {DoneReason}",
                jsonResponse?.Message == null,
                jsonResponse?.Message?.Content ?? "NULL",
                jsonResponse?.DoneReason ?? "null");

            // Handle tool calls if present
            if (jsonResponse?.Message?.ToolCalls != null && jsonResponse.Message.ToolCalls.Count > 0
                && request.ToolExecutor != null)
            {
                _logger.LogInformation("[Ollama] Tool calls detected: {Count}", jsonResponse.Message.ToolCalls.Count);
                return await ExecuteToolCallsAndCompleteAsync(request, model, messages, jsonResponse, ct);
            }

            if (string.IsNullOrEmpty(jsonResponse?.Message?.Content))
            {
                _logger.LogWarning("[Ollama] Empty response from model {Model} (DoneReason: {DoneReason})",
                    model, jsonResponse?.DoneReason ?? "null");
            }

            return ApiResponse<LLMResponse>.SuccessResponse(new LLMResponse
            {
                Content = jsonResponse?.Message?.Content ?? "",
                Provider = LLMProvider.Ollama,
                Model = model,
                TokensUsed = jsonResponse?.EvalCount
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ollama completion failed for model {Model}", model);
            return ApiResponse<LLMResponse>.ErrorResponse($"Ollama service unavailable ({model})", 503);
        }
    }

    /// <summary>
    /// Exécute les tool calls retournés par Ollama, puis fait un second appel avec les résultats.
    /// </summary>
    private async Task<ApiResponse<LLMResponse>> ExecuteToolCallsAndCompleteAsync(
        LLMRequest request,
        string model,
        List<object> messages,
        OllamaResponse firstResponse,
        CancellationToken ct)
    {
        try
        {
            // Build the extended message list: add assistant message with tool_calls
            var extendedMessages = new List<object>(messages);

            // Append assistant message that triggered tool calls (Ollama format)
            extendedMessages.Add(new
            {
                role = "assistant",
                content = firstResponse.Message!.Content ?? "",
                tool_calls = firstResponse.Message.ToolCalls!.Select(tc => new
                {
                    function = new
                    {
                        name = tc.Function?.Name ?? "",
                        arguments = tc.Function?.Arguments
                    }
                }).ToList()
            });

            // Execute each tool call and append results
            foreach (var toolCall in firstResponse.Message.ToolCalls!)
            {
                var toolName = toolCall.Function?.Name ?? "";
                var argsJson = toolCall.Function?.Arguments.ValueKind != JsonValueKind.Undefined
                    ? toolCall.Function!.Arguments.GetRawText()
                    : "{}";

                _logger.LogInformation("[Ollama] Executing tool: {ToolName} with args: {Args}", toolName, argsJson);

                string toolResult;
                try
                {
                    toolResult = await request.ToolExecutor!(toolName, argsJson);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Ollama] Tool execution failed for {ToolName}", toolName);
                    toolResult = JsonSerializer.Serialize(new { error = ex.Message });
                }

                extendedMessages.Add(new
                {
                    role = "tool",
                    content = toolResult
                });
            }

            // Second call to Ollama without tools to get final response
            var followUpRequest = new
            {
                model = model,
                messages = extendedMessages,
                stream = false,
                options = new { temperature = request.Temperature ?? 0.7 }
            };

            var response = await _httpClient.PostAsJsonAsync("/api/chat", followUpRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[Ollama] Follow-up after tool calls failed: {StatusCode} - {Error}", response.StatusCode, error);
                return ApiResponse<LLMResponse>.ErrorResponse($"Ollama tool follow-up error: {error}", (int)response.StatusCode);
            }

            var rawJson = await response.Content.ReadAsStringAsync(ct);
            var finalResponse = JsonSerializer.Deserialize<OllamaResponse>(rawJson);

            return ApiResponse<LLMResponse>.SuccessResponse(new LLMResponse
            {
                Content = finalResponse?.Message?.Content ?? "",
                Provider = LLMProvider.Ollama,
                Model = model,
                TokensUsed = finalResponse?.EvalCount
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Ollama] Tool call loop failed for model {Model}", model);
            return ApiResponse<LLMResponse>.ErrorResponse($"Ollama tool execution failed ({model})", 503);
        }
    }

    /// <summary>
    /// Construit la liste de messages en ajoutant le system prompt en tête si présent.
    /// </summary>
    private static List<object> BuildMessageList(LLMRequest request)
    {
        var messages = new List<object>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new { role = "system", content = request.SystemPrompt });
        }

        foreach (var m in request.Messages)
        {
            messages.Add(new { role = m.Role, content = m.Content });
        }

        return messages;
    }

    private bool ShouldTryFallback(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage)) return false;
        var lowerError = errorMessage.ToLowerInvariant();
        return lowerError.Contains("memory") ||
               lowerError.Contains("system memory") ||
               lowerError.Contains("not found") ||
               lowerError.Contains("unavailable");
    }

    public async Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default)
    {
        var primaryModel = request.Model ?? _options.Model;
        var fallbackModel = _options.FallbackModel;

        try
        {
            await TryStreamWithModelAsync(request, primaryModel, onChunk, ct);
        }
        catch (Exception ex) when (ShouldTryFallback(ex.Message))
        {
            _logger.LogWarning("Primary model {Primary} streaming failed, trying fallback {Fallback}: {Error}",
                primaryModel, fallbackModel, ex.Message);
            await TryStreamWithModelAsync(request, fallbackModel, onChunk, ct);
            _logger.LogInformation("Fallback model {Fallback} streaming succeeded", fallbackModel);
        }
    }

    private async Task TryStreamWithModelAsync(LLMRequest request, string model, Func<string, Task> onChunk, CancellationToken ct)
    {
        var messages = BuildMessageList(request);

        var ollamaRequest = new
        {
            model = model,
            messages = messages,
            stream = true
        };

        var response = await _httpClient.PostAsJsonAsync("/api/chat", ollamaRequest, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line);
                if (chunk?.Message?.Content != null)
                {
                    await onChunk(chunk.Message.Content);
                }
            }
            catch (JsonException)
            {
                // Ignore malformed chunks
            }
        }
    }
}
