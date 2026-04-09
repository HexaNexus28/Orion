using System.Text.Json;
using Orion.Daemon.Core.Entities;

namespace Orion.Daemon.Core.Interfaces;

public interface IActionRegistry
{
    void Register(IAction action);
    IAction? Get(string actionName);
    IEnumerable<string> GetAllActions();
}
