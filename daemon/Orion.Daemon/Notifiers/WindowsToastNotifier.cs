using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Notifiers;

/// <summary>
/// WindowsToastNotifier - Notifications modernes Windows 10/11 via PowerShell
/// Utilise BurntToast module ou Windows.UI.Notifications natif
/// </summary>
public class WindowsToastNotifier : INotifier
{
    private readonly ILogger _logger;

    public string Name => "WindowsToastNotifier";
    public bool IsAvailable => OperatingSystem.IsWindows();

    public WindowsToastNotifier(ILogger logger)
    {
        _logger = logger;
    }

    public async Task NotifyAsync(string title, string message, NotificationPriority priority = NotificationPriority.Normal)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("[WindowsToastNotifier] Not available on this platform");
            return;
        }

        try
        {
            // Utiliser Windows.UI.Notifications via PowerShell (natif, pas de dépendance)
            var escapedTitle = title.Replace("'", "''").Replace("\"", "`\"");
            var escapedMessage = message.Replace("'", "''").Replace("\"", "`\"");

            // Toast XML pour notification moderne
            var toastXml = $@"<toast><visual><binding template='ToastGeneric'><text>{escapedTitle}</text><text>{escapedMessage}</text></binding></visual></toast>";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime]::GetDefault().CreateToastNotifier('ORION').Show([Windows.UI.Notifications.ToastNotification]::new([xml.xmlDocument]::new().LoadXml('{toastXml}')))\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }

            _logger.LogInformation("[WindowsToastNotifier] Toast sent: {Title}", title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WindowsToastNotifier] Failed to send toast, falling back to legacy");
            // Fallback: utiliser WindowsNotifier legacy (MessageBox)
            await NotifyLegacyAsync(title, message, priority);
        }
    }

    private Task NotifyLegacyAsync(string title, string message, NotificationPriority priority)
    {
        // Fallback simple via msg.exe ou net send
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "msg",
                Arguments = $"* /TIME:5 \"ORION - {title}: {message}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        catch { }
        return Task.CompletedTask;
    }

    public Task SpeakAsync(string text)
    {
        // Ce notifier ne fait pas de TTS
        return Task.CompletedTask;
    }
}
