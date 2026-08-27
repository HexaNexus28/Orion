using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Core.Security;

namespace Orion.Daemon.Actions;

public class ListFilesAction : IAction
{
    private readonly PathScope _perimetre;

    public ListFilesAction(DaemonOptions options)
    {
        _perimetre = new PathScope(options.AllowedRoots, options.DeniedNames);
    }

    public string Name => "list_files";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var path = payload.TryGetProperty("path", out var p) ? p.GetString() : null;

        var fullPath = _perimetre.Resoudre(path, out var raison);
        if (fullPath is null)
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, raison));

        var pattern = payload.TryGetProperty("pattern", out var pat) ? pat.GetString() ?? "*" : "*";
        var recursive = payload.TryGetProperty("recursive", out var rec) && rec.GetBoolean();

        try
        {
            if (!Directory.Exists(fullPath))
                return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, $"Directory not found: {fullPath}"));

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // Le filtre s'applique aussi au LISTING : révéler qu'un `.ssh` existe, ou énumérer
            // les `.env` d'un dépôt, renseigne l'attaquant même sans en lire le contenu. Le
            // récursif rendrait l'omission d'autant plus visible.
            var files = Directory.GetFiles(fullPath, pattern, searchOption)
                .Where(_perimetre.EstVisible)
                .Select(f => new FileInfo(f))
                .Select(f => new { name = f.Name, path = f.FullName, size = f.Length, isDirectory = false, modified = f.LastWriteTimeUtc });

            var dirs = Directory.GetDirectories(fullPath, "*", searchOption)
                .Where(_perimetre.EstVisible)
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
