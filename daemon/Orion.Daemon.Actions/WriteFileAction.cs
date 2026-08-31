using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Core.Security;

namespace Orion.Daemon.Actions;

public class WriteFileAction : IAction
{
    private readonly PathScope _scope;

    public WriteFileAction(DaemonOptions options)
    {
        // `options` était injecté ici sans jamais être lu — constat C2 de l'audit du 2026-08-27.
        //
        // Périmètre d'ÉCRITURE distinct de celui de lecture : écrire là où on lit est souvent
        // trop large. Vide, il retombe sur AllowedRoots — donc vers un ensemble plus petit ou
        // égal, jamais vers « tout ».
        var racines = options.AllowedWriteRoots.Length > 0
            ? options.AllowedWriteRoots
            : options.AllowedRoots;

        _scope = new PathScope(racines, options.DeniedNames);
    }

    public string Name => "write_file";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var path = payload.TryGetProperty("path", out var p) ? p.GetString() : null;
        var content = payload.TryGetProperty("content", out var c) ? c.GetString() : null;

        // Le périmètre AVANT toute écriture — et surtout avant CreateDirectory, qui fabriquait
        // l'arborescence de n'importe quel chemin au passage. Cible évidente : le dossier
        // Démarrage, d'où le daemon lui-même est lancé — un fichier déposé là s'exécute à la
        // prochaine ouverture de session.
        var fullPath = _scope.Resolve(path, out var raison);
        if (fullPath is null)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, raison));
        }

        try
        {
            var dossier = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dossier))
            {
                Directory.CreateDirectory(dossier);
            }

            var texte = content ?? "";
            File.WriteAllText(fullPath, texte);

            var data = new
            {
                path = fullPath,
                written = true,
                bytes = texte.Length
            };

            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }
}
