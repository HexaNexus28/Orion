namespace Orion.Core.DTOs;

/// <summary>
/// Information sur un tool disponible
/// </summary>
public class ToolInfoDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InputSchema { get; set; } = string.Empty;
}
