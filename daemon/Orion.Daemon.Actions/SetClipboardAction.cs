using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class SetClipboardAction : IAction
{
    public string Name => "set_clipboard";

    public async Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var text = payload.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

        try
        {
            // Use -EncodedCommand so no user content touches the command-line argument
            var script = $"Set-Clipboard -Value @'\n{text}\n'@";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)!;
            await proc.WaitForExitAsync();

            return DaemonResponse.SuccessResponse(correlationId, new { set = true, length = text.Length });
        }
        catch (Exception ex)
        {
            return DaemonResponse.ErrorResponse(correlationId, ex.Message);
        }
    }
}
