namespace Orion.Core.DTOs.Responses;

/// <summary>Une ligne de la file, telle que l'UI la montre.</summary>
public class DeferredActionDto
{
    public Guid Id { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
    public string Status { get; set; } = string.Empty;
    public bool IsDestructive { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string? RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Ce que le drain a réellement fait. Rendu à la couche API, qui seule sait notifier :
/// le service ne connaît pas le canal SSE, et ne doit pas le connaître.
/// </summary>
public class DeferredDrainReport
{
    public List<DeferredActionDto> Executed { get; set; } = new();
    public List<DeferredActionDto> Failed { get; set; } = new();

    /// <summary>Destructives : le daemon est revenu, mais elles se redemandent avant de partir.</summary>
    public List<DeferredActionDto> AwaitingConfirmation { get; set; } = new();

    public int Expired { get; set; }

    public bool RienAFaire => Executed.Count == 0 && Failed.Count == 0
                              && AwaitingConfirmation.Count == 0 && Expired == 0;
}
