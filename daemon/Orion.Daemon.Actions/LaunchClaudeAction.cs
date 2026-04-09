using System.Diagnostics;
using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class LaunchClaudeAction : IAction
{
    public string Name => "launch_claude";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "https://claude.ai",
                UseShellExecute = true
            };

            Process.Start(psi);

            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, new { opened = true, url = "https://claude.ai" }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }
}
