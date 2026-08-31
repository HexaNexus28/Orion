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

                // Constat M1 de l'audit du 2026-08-27 : un nom peut désigner N processus.
                // `kill_process("chrome")` fermait TOUTES les fenêtres du navigateur alors que la
                // confirmation n'annonçait qu'un nom — l'utilisateur validait autre chose que ce
                // qui allait se produire.
                //
                // On refuse et on ÉNUMÈRE, au lieu d'agir large en silence. Le modèle peut alors
                // viser un PID précis, ou redemander explicitement avec `all: true`.
                var tous = payload.TryGetProperty("all", out var a)
                        && a.ValueKind == JsonValueKind.True;

                if (procs.Length > 1 && !tous)
                {
                    var liste = procs.Select(p2 => $"{p2.ProcessName} (PID {p2.Id})").ToList();
                    foreach (var p2 in procs) p2.Dispose();

                    return DaemonResponse.ErrorResponse(correlationId,
                        $"« {name} » correspond a {procs.Length} processus : {string.Join(", ", liste)}. "
                        + "Precise un `pid`, ou redemande avec `all: true` pour tous les terminer.");
                }

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
