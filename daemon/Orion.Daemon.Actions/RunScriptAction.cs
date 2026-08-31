using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class RunScriptAction : IAction
{
    private readonly int _timeoutSecondes;

    public RunScriptAction(DaemonOptions options)
    {
        _timeoutSecondes = Math.Max(1, options.ScriptTimeoutSeconds);
    }

    public string Name => "run_script";

    public async Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var script = payload.TryGetProperty("script", out var s) ? s.GetString() : null;
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

                // -EncodedCommand plutôt que -Command "{script}" — constat E1 de l'audit du
                // 2026-08-27.
                //
                // `run_script` exécute du code arbitraire PAR CONCEPTION ; ce n'était pas ça le
                // problème. Le problème était l'absence d'échappement : un script contenant un
                // guillemet double terminait l'argument, et la suite était relue par
                // powershell.exe comme SES options. La commande réellement lancée cessait alors
                // de correspondre à celle que l'utilisateur avait confirmée — et une
                // confirmation qui porte sur autre chose que ce qui s'exécute n'est pas une
                // confirmation.
                //
                // En base64 d'UTF-16LE, il n'y a plus la moindre frontière de guillemet à casser.
                //
                // -NoProfile : le profil de l'utilisateur pourrait redéfinir des commandes et
                //   changer le sens du script sans que rien ne l'indique.
                // -NonInteractive : un script qui pose une question resterait bloqué à attendre
                //   une réponse que personne ne verra jamais.
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass "
                          + $"-EncodedCommand {EnBase64Utf16(script)}",

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

            // Lecture AVANT l'attente, et en parallèle des deux flux : un script qui écrit plus
            // que la taille du tampon de tube se bloque en écriture pendant qu'on l'attend, et
            // les deux camps s'attendent pour toujours.
            var sortie = process.StandardOutput.ReadToEndAsync();
            var erreur = process.StandardError.ReadToEndAsync();

            using var minuterie = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSecondes));
            try
            {
                await process.WaitForExitAsync(minuterie.Token);
            }
            catch (OperationCanceledException)
            {
                // Le daemon traite les commandes une par une : un script suspendu le rendrait
                // muet sur tout le reste. On tue l'arbre et on le DIT, plutôt que de laisser
                // ORION paraître mort.
                try { process.Kill(entireProcessTree: true); } catch { /* déjà parti */ }

                return DaemonResponse.ErrorResponse(correlationId,
                    $"Script interrompu apres {_timeoutSecondes} s (Daemon:ScriptTimeoutSeconds).");
            }

            var data = new
            {
                exitCode = process.ExitCode,
                output = await sortie,
                error = await erreur,
                success = process.ExitCode == 0
            };

            return DaemonResponse.SuccessResponse(correlationId, data);
        }
        catch (Exception ex)
        {
            return DaemonResponse.ErrorResponse(correlationId, ex.Message);
        }
    }

    /// <summary>
    /// PowerShell attend de l'UTF-16 little-endian encodé en base64 — pas de l'UTF-8. Se tromper
    /// d'encodage ne lève rien : le script part en caractères illisibles.
    /// </summary>
    private static string EnBase64Utf16(string script)
        => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
