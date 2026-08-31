using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Core.Security;

namespace Orion.Daemon.Actions;

public class ReadFileAction : IAction
{
    private readonly PathScope _scope;

    public ReadFileAction(DaemonOptions options)
    {
        // `options` était injecté ici sans JAMAIS être lu : le garde-fou avait l'air
        // configurable, il n'existait pas. Constat C1 de l'audit du 2026-08-27.
        _scope = new PathScope(options.AllowedRoots, options.DeniedNames);
    }

    public string Name => "read_file";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var path = payload.TryGetProperty("path", out var p) ? p.GetString() : null;

        // Le périmètre AVANT tout accès disque — et on travaille ensuite sur le chemin qu'il
        // renvoie, jamais sur l'entrée : vérifier une chaîne puis en ouvrir une autre est le
        // motif classique du contournement.
        var fullPath = _scope.Resolve(path, out var raison);
        if (fullPath is null)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, raison));
        }

        try
        {
            if (!File.Exists(fullPath))
            {
                return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, $"File not found: {fullPath}"));
            }

            var maxLines = payload.TryGetProperty("maxLines", out var ml) ? ml.GetInt32() : 100;

            // UNE seule lecture. Avant, `File.ReadLines` était appelé TROIS fois (contenu,
            // total, troncature) : trois parcours disque, et trois instants différents — un
            // fichier qui change entre-temps produisait un `truncated` incohérent.
            var toutes = File.ReadLines(fullPath).ToList();
            var lines = toutes.Take(maxLines).ToList();

            var data = new
            {
                path = fullPath,
                lines,
                totalLines = toutes.Count,
                truncated = lines.Count < toutes.Count
            };

            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }
}
