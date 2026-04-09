using System.Diagnostics;
using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class GitStatusAction : IAction
{
    private readonly DaemonOptions _options;

    public GitStatusAction(DaemonOptions options)
    {
        _options = options;
    }

    public string Name => "git_status";

    public async Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var repoPath = payload.TryGetProperty("path", out var p) ? p.GetString() : ".";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain -b",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                return DaemonResponse.ErrorResponse(correlationId, $"Git error: {error}");
            }

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var branchLine = lines.FirstOrDefault(l => l.StartsWith("##"));
            var branch = branchLine?.Substring(3).Split(' ')[0] ?? "unknown";
            var changes = lines.Where(l => !l.StartsWith("##")).ToList();

            var data = new
            {
                path = repoPath,
                branch = branch,
                changes = changes,
                hasChanges = changes.Any()
            };

            return DaemonResponse.SuccessResponse(correlationId, data);
        }
        catch (Exception ex)
        {
            return DaemonResponse.ErrorResponse(correlationId, ex.Message);
        }
    }
}
