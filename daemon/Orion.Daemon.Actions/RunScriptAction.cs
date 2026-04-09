using System.Diagnostics;
using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class RunScriptAction : IAction
{
    private readonly DaemonOptions _options;

    public RunScriptAction(DaemonOptions options)
    {
        _options = options;
    }

    public string Name => "run_script";

    public async Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var script = payload.GetProperty("script").GetString();
        var workingDir = payload.TryGetProperty("workingDir", out var wd) ? wd.GetString() : null;

        if (string.IsNullOrEmpty(script))
        {
            return DaemonResponse.ErrorResponse(correlationId, "Missing script");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -Command \"{script}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(workingDir))
            {
                psi.WorkingDirectory = workingDir;
            }

            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var data = new
            {
                exitCode = process.ExitCode,
                output = output,
                error = error,
                success = process.ExitCode == 0
            };

            return DaemonResponse.SuccessResponse(correlationId, data);
        }
        catch (Exception ex)
        {
            return DaemonResponse.ErrorResponse(correlationId, ex.Message);
        }
    }
}
