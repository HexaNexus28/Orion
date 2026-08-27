using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

/// <summary>
/// Ce sur quoi l'utilisateur travaille EN CE MOMENT : application au premier plan et titre de sa
/// fenêtre.
///
/// POURQUOI CETTE ACTION EXISTE. `system_status` répond « quelle machine » — nom, mémoire, durée
/// de fonctionnement. Aucune de ces informations ne dit ce que la personne est en train de FAIRE.
/// ProcessWatcher connaissait déjà l'application active, mais ne remontait que son nom de
/// processus : « Code ». ORION savait donc que l'utilisateur codait, sans savoir sur quoi.
///
/// Le titre de fenêtre change cela : « useVAD.ts - ShiftCore - Visual Studio Code » donne le
/// fichier ET le projet. À partir de là ORION peut lire le bon diff, lancer les tests du bon
/// dépôt, proposer le bon message de commit. C'est la différence entre un indicateur d'activité
/// et un assistant qui travaille avec toi.
///
/// LE TITRE EST RENVOYÉ BRUT, sans interprétation. Chaque éditeur a son format, et ils changent
/// au fil des versions ; faire l'analyse ici obligerait à redéployer le daemon — donc à passer
/// sur la machine de l'utilisateur — à chaque ajustement. Le backend, lui, se redéploie tout
/// seul en deux minutes.
/// </summary>
public class GetWorkContextAction : IAction
{
    public string Name => "work_context";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        try
        {
            var fenetre = GetForegroundWindow();

            // Aucune fenêtre au premier plan : session verrouillée, ou bureau vide. Ce n'est pas
            // une erreur — c'est une information, et ORION doit pouvoir la distinguer d'une panne.
            if (fenetre == IntPtr.Zero)
            {
                return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, new
                {
                    active = false,
                    application = (string?)null,
                    windowTitle = (string?)null,
                    capturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                }));
            }

            var tampon = new StringBuilder(512);
            var longueur = GetWindowText(fenetre, tampon, tampon.Capacity);
            var titre = longueur > 0 ? tampon.ToString() : null;

            string? application = null;
            _ = GetWindowThreadProcessId(fenetre, out var pid);
            if (pid != 0)
            {
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    application = process.ProcessName;
                }
                catch (ArgumentException)
                {
                    // Le processus vient de se terminer entre les deux appels. Le titre reste
                    // valable, on ne perd pas tout pour autant.
                }
            }

            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, new
            {
                active = true,
                application,
                windowTitle = titre,
                capturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder texte, int taille);
}
