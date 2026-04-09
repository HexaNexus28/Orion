using Orion.Core.Common;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Service de transcription audio locale via Whisper
/// Phase 4 : Voix temps réel - STT local gratuit
/// </summary>
public interface IWhisperService
{
    /// <summary>
    /// Transcrit un fichier audio en texte
    /// </summary>
    /// <param name="audioStream">Stream audio (WAV, MP3, WebM, etc.)</param>
    /// <param name="language">Langue optionnelle (ex: "fr", "en")</param>
    /// <returns>Texte transcrit</returns>
    Task<Result<string>> TranscribeAsync(Stream audioStream, string? language = null);

    /// <summary>
    /// Transcrit des données audio brutes
    /// </summary>
    /// <param name="audioData">Données audio (bytes)</param>
    /// <param name="language">Langue optionnelle</param>
    /// <returns>Texte transcrit</returns>
    Task<Result<string>> TranscribeAsync(byte[] audioData, string? language = null);

    /// <summary>
    /// Vérifie si le modèle Whisper est chargé et prêt
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Langues supportées
    /// </summary>
    IReadOnlyList<string> SupportedLanguages { get; }
}
