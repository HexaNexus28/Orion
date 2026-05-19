using System.Diagnostics;
using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class KillProcessAction : IAction
{
    public string Name => "kill_process";

    public async Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var name = payload.TryGetProperty("name", out var n) ? n.GetString() : null;
        int? pid = payload.TryGetProperty("pid", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

        if (string.IsNullOrWhiteSpace(name) && pid == null)
            return DaemonResponse.ErrorResponse(correlationId, "Missing name or pid");

        try
        {
            var killed = new List<string>();

            if (pid.HasValue)
            {
                using var proc = Process.GetProcessById(pid.Value);
                var procName = proc.ProcessName;
                proc.Kill(entireProcessTree: true);
                await proc.WaitForExitAsync();
                killed.Add($"{procName} (PID {pid.Value})");
            }
            else
            {
                var procs = Process.GetProcessesByName(name!.Replace(".exe", "", StringComparison.OrdinalIgnoreCase));
                if (procs.Length == 0)
                    return DaemonResponse.ErrorResponse(correlationId, $"No process found: {name}");

                foreach (var proc in procs)
                {
                    using (proc)
                    {
                        var procName = proc.ProcessName;
                        var procId = proc.Id;
                        proc.Kill(entireProcessTree: true);
                        await proc.WaitForExitAsync();
                        killed.Add($"{procName} (PID {procId})");
                    }
                }
            }

            return DaemonResponse.SuccessResponse(correlationId, new { killed, count = killed.Count });
        }
        catch (Exception ex)
        {
            return DaemonResponse.ErrorResponse(correlationId, ex.Message);
        }
    }
}
