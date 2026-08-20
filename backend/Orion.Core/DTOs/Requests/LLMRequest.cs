using System.Text.Json.Nodes;

namespace Orion.Core.DTOs.Requests;

public class LLMRequest
{
    public string SystemPrompt { get; set; } = string.Empty;
    public List<LLMMessage> Messages { get; set; } = new();
    public string? Model { get; set; }
    public float? Temperature { get; set; }
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Définitions des tools disponibles pour le LLM (format JSON Schema)
    /// </summary>
    public List<ToolDefinition>? Tools { get; set; }

    /// <summary>
    /// Callback pour exécuter un tool : (toolName, argsJson) => resultJson
    /// Géré par ConversationAgent
    /// </summary>
    public Func<string, string, Task<string>>? ToolExecutor { get; set; }
}

public class LLMMessage
{
    public string Role { get; set; } = "user"; // 'system', 'user', 'assistant', 'tool'
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Appels d'outils portés par un message 'assistant'. Doivent être réinjectés tels quels
    /// dans l'historique pour que le modèle relie ses demandes aux résultats.
    /// </summary>
    public List<Internal.LLM.LLMToolCall>? ToolCalls { get; set; }

    /// <summary>Corrélation d'un message 'tool' avec l'appel qui l'a déclenché.</summary>
    public string? ToolCallId { get; set; }

    /// <summary>Nom de l'outil pour un message 'tool'.</summary>
    public string? ToolName { get; set; }
}

public class ToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public JsonObject Parameters { get; set; } = new();
}
