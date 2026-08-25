namespace Orion.Core.DTOs.Responses;

public enum AgentEventType
{
    /// <summary>Fragment de texte généré par le LLM.</summary>
    Token,

    /// <summary>Un outil va être exécuté.</summary>
    ToolStart,

    /// <summary>Un outil vient de terminer.</summary>
    ToolResult,

    /// <summary>Le tour est terminé — plus aucun outil demandé.</summary>
    Done,

    /// <summary>Erreur fatale du tour.</summary>
    Error
}

/// <summary>
/// Événement typé émis par la boucle agent. Remplace le flux de texte brut :
/// l'UI peut enfin montrer CE QUE fait ORION, pas seulement ce qu'il dit.
/// </summary>
public class AgentEvent
{
    public AgentEventType Type { get; set; }

    /// <summary>Texte du token (Type = Token) ou message d'erreur (Type = Error).</summary>
    public string? Text { get; set; }

    public string? ToolName { get; set; }

    /// <summary>Arguments JSON de l'appel (Type = ToolStart).</summary>
    public string? ToolArgs { get; set; }

    /// <summary>Succès de l'exécution (Type = ToolResult).</summary>
    public bool? ToolOk { get; set; }

    /// <summary>Résultat tronqué, destiné à l'affichage (Type = ToolResult).</summary>
    public string? ToolSummary { get; set; }

    /// <summary>Numéro d'itération de la boucle, à partir de 1.</summary>
    public int Iteration { get; set; }

    public static AgentEvent Token(string text, int iteration)
        => new() { Type = AgentEventType.Token, Text = text, Iteration = iteration };

    public static AgentEvent ToolStart(string name, string argsJson, int iteration)
        => new() { Type = AgentEventType.ToolStart, ToolName = name, ToolArgs = argsJson, Iteration = iteration };

    public static AgentEvent ToolResult(string name, bool ok, string summary, int iteration)
        => new() { Type = AgentEventType.ToolResult, ToolName = name, ToolOk = ok, ToolSummary = summary, Iteration = iteration };

    public static AgentEvent Done(int iteration)
        => new() { Type = AgentEventType.Done, Iteration = iteration };

    public static AgentEvent Error(string message, int iteration)
        => new() { Type = AgentEventType.Error, Text = message, Iteration = iteration };
}
