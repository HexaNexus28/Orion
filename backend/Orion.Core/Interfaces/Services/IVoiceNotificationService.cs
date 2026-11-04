namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Service for text-to-speech synthesis via daemon (Kokoro TTS)
/// </summary>
public interface IVoiceNotificationService
{
    /// <summary>
    /// Send TTS request to daemon to speak text aloud
    /// </summary>
    Task SpeakAsync(string text, CancellationToken ct = default);
}
