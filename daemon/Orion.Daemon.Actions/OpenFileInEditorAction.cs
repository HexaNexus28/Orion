using System.Diagnostics;
using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class OpenFileInEditorAction : IAction
{
    private readonly DaemonOptions _options;

    public OpenFileInEditorAction(DaemonOptions options)
    {
        _options = options;
    }

    public string Name => "open_file";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var filePath = payload.GetProperty("path").GetString();
        var editor = payload.TryGetProperty("editor", out var ed) ? ed.GetString() : "code";

        if (string.IsNullOrEmpty(filePath))
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, "Missing file path"));
        }

        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var psi = new ProcessStartInfo
            {
                FileName = editor,
                Arguments = $"\"{fullPath}\"",
                UseShellExecute = true
            };

            var process = Process.Start(psi);

            var data = new
            {
                file = fullPath,
                editor = editor,
                processId = process?.Id,
                opened = true
            };

            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }
}
