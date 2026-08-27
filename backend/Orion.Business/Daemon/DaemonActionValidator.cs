using Orion.Core.DTOs.Requests;
using Orion.Core.Interfaces.Daemon;

namespace Orion.Business.Daemon;

public class DaemonActionValidator
{
    private readonly HashSet<string> _allowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "open_app",
        "open_file",
        "run_script",
        "open_url",
        "launch_claude",
        "system_status",

        // Lecture seule : application au premier plan et titre de sa fenetre. Aucun effet sur la
        // machine. Le chemin par OUTIL fonctionnait deja — cette liste ne protege que l endpoint
        // direct /api/daemon/action, et une action absente ici serait invisible depuis le front.
        "work_context",
        "read_file",
        "write_file",
        "git_status",
        "git_commit",
        "list_files",
        "kill_process",
        "get_clipboard",
        "set_clipboard",
        "type_text",
        "capture_screen",
        "proactive_deferred",
    };

    public bool IsAllowed(string action)
    {
        return _allowedActions.Contains(action);
    }

    public void ValidateOrThrow(DaemonActionRequest action)
    {
        if (!IsAllowed(action.Action))
        {
            throw new InvalidOperationException($"Action '{action.Action}' is not in whitelist");
        }
    }
}
