using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class ListFilesAction : IAction
{
    public string Name => "list_files";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var path = payload.TryGetProperty("path", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, "Missing path"));

        var pattern = payload.TryGetProperty("pattern", out var pat) ? pat.GetString() ?? "*" : "*";
        var recursive = payload.TryGetProperty("recursive", out var rec) && rec.GetBoolean();

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
                return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, $"Directory not found: {fullPath}"));

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var files = Directory.GetFiles(fullPath, pattern, searchOption)
                .Select(f => new FileInfo(f))
                .Select(f => new { name = f.Name, path = f.FullName, size = f.Length, isDirectory = false, modified = f.LastWriteTimeUtc });

            var dirs = Directory.GetDirectories(fullPath, "*", searchOption)
                .Select(d => new DirectoryInfo(d))
                .Select(d => new { name = d.Name, path = d.FullName, size = 0L, isDirectory = true, modified = d.LastWriteTimeUtc });

            var entries = dirs.Cast<object>().Concat(files.Cast<object>()).ToList();
            var data = new { path = fullPath, entries, count = entries.Count };
            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }
}
