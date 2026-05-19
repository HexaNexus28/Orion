using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class TypeTextAction : IAction
{
    public string Name => "type_text";

    public async Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var text = payload.TryGetProperty("text", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(text))
            return DaemonResponse.ErrorResponse(correlationId, "Missing text");

        var delayMs = payload.TryGetProperty("delayMs", out var d) ? d.GetInt32() : 500;

        try
        {
            var escaped = EscapeSendKeys(text);
            var script = $"Start-Sleep -Milliseconds {delayMs}\n$wsh = New-Object -ComObject WScript.Shell\n$wsh.SendKeys('{escaped}')";
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

            return DaemonResponse.SuccessResponse(correlationId, new { typed = true, length = text.Length });
        }
        catch (Exception ex)
        {
            return DaemonResponse.ErrorResponse(correlationId, ex.Message);
        }
    }

    private static string EscapeSendKeys(string text)
    {
        // WScript.Shell SendKeys special chars: + ^ % ~ { } ( ) [ ]
        var sb = new StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '+' => "{+}",
                '^' => "{^}",
                '%' => "{%}",
                '~' => "{~}",
                '{' => "{{}",
                '}' => "{}}",
                '(' => "{(}",
                ')' => "{)}",
                '[' => "{[}",
                ']' => "{]}",
                '\'' => "''",
                _ => c.ToString()
            });
        }
        return sb.ToString();
    }
}
