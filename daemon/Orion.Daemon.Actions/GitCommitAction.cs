using System.Diagnostics;
using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class GitCommitAction : IAction
{
    private readonly DaemonOptions _options;

    public GitCommitAction(DaemonOptions options)
    {
        _options = options;
    }

    public string Name => "git_commit";

    public async Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var repoPath = payload.TryGetProperty("path", out var p) ? p.GetString() : ".";
        var message = payload.GetProperty("message").GetString();

        if (string.IsNullOrEmpty(message))
        {
            return DaemonResponse.ErrorResponse(correlationId, "Missing commit message");
        }

        try
        {
            // Stage all changes
            var stagePsi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "add -A",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using (var stage = Process.Start(stagePsi)!) { await stage.WaitForExitAsync(); }

            // Commit
            var commitPsi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"commit -m \"{message.Replace("\"", "\\\"")}\"",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(commitPsi)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var data = new
            {
                success = process.ExitCode == 0,
                output = output,
                error = error,
                message = message
            };

            return DaemonResponse.SuccessResponse(correlationId, data);
        }
        catch (Exception ex)
        {
            return DaemonResponse.ErrorResponse(correlationId, ex.Message);
        }
    }
}
