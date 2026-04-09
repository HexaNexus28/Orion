namespace Orion.Core.Interfaces.Services;

// Scaffold - to be fully implemented
public interface IPushNotificationService
{
    Task SendNotificationAsync(string title, string body, Dictionary<string, string>? data = null, CancellationToken ct = default);
}
