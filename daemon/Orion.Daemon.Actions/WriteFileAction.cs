using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class WriteFileAction : IAction
{
    private readonly DaemonOptions _options;

    public WriteFileAction(DaemonOptions options)
    {
        _options = options;
    }

    public string Name => "write_file";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var path = payload.GetProperty("path").GetString();
        var content = payload.GetProperty("content").GetString();

        if (string.IsNullOrEmpty(path))
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, "Missing path"));
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content ?? "");

            var data = new
            {
                path = fullPath,
                written = true,
                bytes = (content ?? "").Length
            };

            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }
}
