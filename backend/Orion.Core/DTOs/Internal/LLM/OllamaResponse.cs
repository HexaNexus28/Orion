using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orion.Core.DTOs.Internal.LLM;

/// <summary>
/// Chunk NDJSON de l'endpoint natif Ollama `/api/chat` en mode stream.
/// Seul le streaming subsiste : le chemin non-streamé a disparu avec l'ancien `OllamaClient`.
/// </summary>
public class OllamaStreamChunk
{
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; set; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; set; }
}

public class OllamaMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OllamaToolCall>? ToolCalls { get; set; }
}

public class OllamaToolCall
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("function")]
    public OllamaToolCallFunction? Function { get; set; }
}

public class OllamaToolCallFunction
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Ollama natif renvoie un OBJET JSON ici (l'API OpenAI renvoie une chaîne).</summary>
    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }
}
