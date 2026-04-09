using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class ActionRegistry : IActionRegistry
{
    private readonly Dictionary<string, IAction> _actions = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IAction action)
    {
        _actions[action.Name] = action;
    }

    public IAction? Get(string actionName)
    {
        return _actions.TryGetValue(actionName, out var action) ? action : null;
    }

    public IEnumerable<string> GetAllActions()
    {
        return _actions.Keys;
    }
}
