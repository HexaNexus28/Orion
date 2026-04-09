namespace Orion.Core.Interfaces.Tools;

public interface IToolRegistry
{
    ITool? GetTool(string name);
    IEnumerable<ITool> GetAllTools();
    void RegisterTool(ITool tool);
}
