using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// Transcription par Voxtral (Mistral). Voir <see cref="TranscriptionOptions"/> pour les mesures
/// qui ont motive ce choix.
/// </summary>
public class VoxtralTranscriptionService : IWhisperService
{
    public const string HttpClientName = "Voxtral";

    private readonly IHttpClientFactory _fabrique;
    private readonly TranscriptionOptions _options;
    private readonly ILogger<VoxtralTranscriptionService> _logger;

    public VoxtralTranscriptionService(
        IHttpClientFactory fabrique,
        IOptions<TranscriptionOptions> options,
        ILogger<VoxtralTranscriptionService> logger)
    {
        _fabrique = fabrique;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Pret sans chargement : rien a telecharger, rien a garder en memoire.</summary>
    public bool IsReady => _options.Enabled && _options.IsConfigured;

    /// <summary>Voxtral detecte la langue seul ; la liste reste celle attendue par l interface.</summary>
    public IReadOnlyList<string> SupportedLanguages { get; } = new[] { "fr", "en", "es", "de", "it", "pt", "nl" };

    public async Task<ApiResponse<string>> TranscribeAsync(Stream audioStream, string? language = null)
    {
        using var memoire = new MemoryStream();
        await audioStream.CopyToAsync(memoire);
        return await TranscribeAsync(memoire.ToArray(), language);
    }

    public async Task<ApiResponse<string>> TranscribeAsync(byte[] audioData, string? language = null)
    {
        if (!IsReady)
            return ApiResponse<string>.ErrorResponse("Transcription distante non configuree", 503);

        if (audioData.Length == 0)
            return ApiResponse<string>.ErrorResponse("Audio vide", 400);

        try
        {
            var client = _fabrique.CreateClient(HttpClientName);

            using var contenu = new MultipartFormDataContent();
            var fichier = new ByteArrayContent(audioData);
            fichier.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            contenu.Add(fichier, "file", "audio.wav");
            contenu.Add(new StringContent(_options.Model), "model");

            // La langue est transmise quand on la connait : la faire deviner coute un temps de
            // detection pour un resultat qu on avait deja.
            if (!string.IsNullOrWhiteSpace(language) && language != "auto")
                contenu.Add(new StringContent(language), "language");

            var debut = DateTime.UtcNow;
            using var reponse = await client.PostAsync("audio/transcriptions", contenu);
            var corps = await reponse.Content.ReadAsStringAsync();

            if (!reponse.IsSuccessStatusCode)
            {
                // Journalise en AVERTISSEMENT, pas en erreur : la cascade a un repli, ce n est
                // pas encore une panne. Le code compte — 429 (quota) se traite autrement qu un 500.
                _logger.LogWarning("[Voxtral] {Code} — {Corps}", (int)reponse.StatusCode,
                    corps.Length > 200 ? corps[..200] : corps);
                return ApiResponse<string>.ErrorResponse($"Voxtral {(int)reponse.StatusCode}", (int)reponse.StatusCode);
            }

            using var doc = JsonDocument.Parse(corps);
            var texte = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;

            _logger.LogInformation("[Voxtral] Transcrit en {Duree:F2} s — {Taille} octets",
                (DateTime.UtcNow - debut).TotalSeconds, audioData.Length);

            return ApiResponse<string>.SuccessResponse(texte.Trim());
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[Voxtral] Delai depasse ({Timeout} s) — repli local", _options.TimeoutSeconds);
            return ApiResponse<string>.ErrorResponse("Delai de transcription depasse", 504);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Voxtral] Echec — repli local");
            return ApiResponse<string>.ErrorResponse(ex.Message, 502);
        }
    }
}