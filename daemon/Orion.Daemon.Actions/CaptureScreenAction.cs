using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class CaptureScreenAction : IAction
{
    public string Name => "capture_screen";

    public async Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var savePath = payload.TryGetProperty("savePath", out var sp) ? sp.GetString() : null;
        var tempPath = string.IsNullOrEmpty(savePath)
            ? Path.Combine(Path.GetTempPath(), $"orion_screen_{DateTime.UtcNow:yyyyMMddHHmmss}.png")
            : Path.GetFullPath(savePath);

        try
        {
            var script = $@"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bitmap = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
$bitmap.Save([System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{Convert.ToBase64String(Encoding.UTF8.GetBytes(tempPath))}')),[System.Drawing.Imaging.ImageFormat]::Png)
Write-Output ($bitmap.Width.ToString() + ',' + $bitmap.Height.ToString())
$graphics.Dispose()
$bitmap.Dispose()
";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi)!;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0 || !File.Exists(tempPath))
                return DaemonResponse.ErrorResponse(correlationId, stderr.Length > 0 ? stderr.Trim() : "Screenshot failed");

            int width = 0, height = 0;
            var dims = stdout.Trim().Split(',');
            if (dims.Length == 2) { int.TryParse(dims[0], out width); int.TryParse(dims[1], out height); }

            var bytes = await File.ReadAllBytesAsync(tempPath);
            var base64 = Convert.ToBase64String(bytes);

            return DaemonResponse.SuccessResponse(correlationId, new
            {
                base64,
                mimeType = "image/png",
                width,
                height,
                savedTo = string.IsNullOrEmpty(savePath) ? (string?)null : tempPath
            });
        }
        catch (Exception ex)
        {
            return DaemonResponse.ErrorResponse(correlationId, ex.Message);
        }
        finally
        {
            if (string.IsNullOrEmpty(savePath) && File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
