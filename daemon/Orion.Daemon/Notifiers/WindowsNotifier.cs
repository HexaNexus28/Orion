using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Notifiers;

/// <summary>
/// WindowsNotifier - Notifications Windows natives (bas à droite)
/// </summary>
public class WindowsNotifier : INotifier
{
    private readonly ILogger _logger;

    public string Name => "WindowsNotifier";
    public bool IsAvailable => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // Windows API
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public WindowsNotifier(ILogger logger)
    {
        _logger = logger;
    }

    public Task NotifyAsync(string title, string message, NotificationPriority priority = NotificationPriority.Normal)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("[WindowsNotifier] Not available on this platform");
            return Task.CompletedTask;
        }

        try
        {
            // Utilise MessageBox comme fallback simple
            // En production, utiliser Windows.UI.Notifications ou WindowsCommunityToolkit
            uint iconType = priority switch
            {
                NotificationPriority.Critical => 0x10u, // MB_ICONERROR
                NotificationPriority.High => 0x30u,       // MB_ICONEXCLAMATION
                _ => 0x40u                                // MB_ICONINFORMATION
            };

            // Mode non-bloquant - lancer dans un thread séparé
            Task.Run(() =>
            {
                try
                {
                    MessageBox(IntPtr.Zero, message, $"ORION - {title}", iconType | 0x1000u); // MB_SETFOREGROUND
                }
                catch { }
            });

            _logger.LogInformation("[WindowsNotifier] Notification sent: {Title}", title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WindowsNotifier] Failed to send notification");
        }

        return Task.CompletedTask;
    }

    public Task SpeakAsync(string text)
    {
        // WindowsNotifier ne fait pas de TTS
        return Task.CompletedTask;
    }
}
