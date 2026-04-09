using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Notifiers;

/// <summary>
/// PowerShellTtsNotifier - TTS via PowerShell (SAPI 5)
/// Avantage: 0 dépendance NuGet, fonctionne sur tous les Windows
/// </summary>
public class PowerShellTtsNotifier : INotifier
{
    private readonly ILogger _logger;

    public string Name => "PowerShellTtsNotifier";
    public bool IsAvailable => OperatingSystem.IsWindows();

    public PowerShellTtsNotifier(ILogger logger)
    {
        _logger = logger;
    }

    public Task NotifyAsync(string title, string message, NotificationPriority priority = NotificationPriority.Normal)
    {
        // Pas de notification visuelle - uniquement TTS
        return Task.CompletedTask;
    }

    public async Task SpeakAsync(string text)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("[PowerShellTtsNotifier] Not available on this platform");
            return;
        }

        try
        {
            _logger.LogInformation("[PowerShellTtsNotifier] Speaking: {Preview}...",
                text.Length > 40 ? text[..40] + "..." : text);

            // Échapper les caractères spéciaux pour PowerShell
            var escapedText = text
                .Replace("'", "''")
                .Replace("\"", "`\"");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-Type -AssemblyName System.Speech; (New-Object System.Speech.Synthesis.SpeechSynthesizer).Speak('{escapedText}')\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                
                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    _logger.LogWarning("[PowerShellTtsNotifier] PowerShell error: {Error}", error);
                }
                else
                {
                    _logger.LogInformation("[PowerShellTtsNotifier] Speech completed");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PowerShellTtsNotifier] Failed to speak");
        }
    }
}
