using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools;

public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new();
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(ILogger<ToolRegistry> logger, IEnumerable<ITool> tools)
    {
        _logger = logger;
        
        // Auto-register all tools from DI
        foreach (var tool in tools)
        {
            RegisterTool(tool);
        }
    }

    public void RegisterTool(ITool tool)
    {
        if (_tools.TryAdd(tool.Name, tool))
        {
            _logger.LogInformation("Tool registered: {ToolName}", tool.Name);
        }
        else
        {
            _logger.LogWarning("Tool already exists: {ToolName}", tool.Name);
        }
    }

    public ITool? GetTool(string name)
    {
        _tools.TryGetValue(name, out var tool);
        return tool;
    }

    public IEnumerable<ITool> GetAllTools()
    {
        return _tools.Values.ToList();
    }
}
