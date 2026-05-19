using System.Diagnostics;
using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class GetClipboardAction : IAction
{
    public string Name => "get_clipboard";

    public async Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-Command \"Get-Clipboard\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)!;
            var text = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            return DaemonResponse.SuccessResponse(correlationId, new { text = text.TrimEnd('\r', '\n') });
        }
        catch (Exception ex)
        {
            return DaemonResponse.ErrorResponse(correlationId, ex.Message);
        }
    }
}
