using System.Text.Json;
using Orion.Daemon.Core.Entities;

namespace Orion.Daemon.Core.Interfaces;

public interface IAction
{
    string Name { get; }
    Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId);
}
