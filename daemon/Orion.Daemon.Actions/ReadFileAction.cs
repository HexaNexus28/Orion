using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class ReadFileAction : IAction
{
    private readonly DaemonOptions _options;

    public ReadFileAction(DaemonOptions options)
    {
        _options = options;
    }

    public string Name => "read_file";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var path = payload.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path))
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, "Missing path"));
        }

        try
        {
            var fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
            {
                return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, $"File not found: {fullPath}"));
            }

            var maxLines = payload.TryGetProperty("maxLines", out var ml) ? ml.GetInt32() : 100;
            var lines = File.ReadLines(fullPath).Take(maxLines).ToList();

            var data = new
            {
                path = fullPath,
                lines = lines,
                totalLines = File.ReadLines(fullPath).Count(),
                truncated = lines.Count < File.ReadLines(fullPath).Count()
            };

            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }
}
