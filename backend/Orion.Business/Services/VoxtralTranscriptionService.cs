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

    private readonly IHttpClientFactory _httpFactory;
    private readonly TranscriptionOptions _options;
    private readonly ILogger<VoxtralTranscriptionService> _logger;

    public VoxtralTranscriptionService(
        IHttpClientFactory httpFactory,
        IOptions<TranscriptionOptions> options,
        ILogger<VoxtralTranscriptionService> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Pret sans chargement : rien a telecharger, rien a garder en memoire.</summary>
    public bool IsReady => _options.Enabled && _options.IsConfigured;

    /// <summary>Voxtral detecte la langue seul ; la liste reste celle attendue par l interface.</summary>
    public IReadOnlyList<string> SupportedLanguages { get; } = new[] { "fr", "en", "es", "de", "it", "pt", "nl" };

    public async Task<ApiResponse<string>> TranscribeAsync(Stream audioStream, string? language = null)
    {
        using var buffer = new MemoryStream();
        await audioStream.CopyToAsync(buffer);
        return await TranscribeAsync(buffer.ToArray(), language);
    }

    public async Task<ApiResponse<string>> TranscribeAsync(byte[] audioData, string? language = null)
    {
        if (!IsReady)
            return ApiResponse<string>.ErrorResponse("Transcription distante non configuree", 503);

        if (audioData.Length == 0)
            return ApiResponse<string>.ErrorResponse("Audio vide", 400);

        try
        {
            var client = _httpFactory.CreateClient(HttpClientName);

            // Pose ICI et non a l enregistrement du client : la fabrique construit ses clients
            // au demarrage, avant que PostConfigure n ait repli la cle depuis Embedding:ApiKey.
            // Un en-tete fige a ce moment-la partirait vide — c est exactement ce qui s est
            // produit en production le 2026-08-27 : « 401 Invalid API Key », et la cascade a
            // bascule sur Whisper local sans que la voix ne tombe.
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using var form = new MultipartFormDataContent();
            var filePart = new ByteArrayContent(audioData);
            filePart.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(filePart, "file", "audio.wav");
            form.Add(new StringContent(_options.Model), "model");

            // La langue est transmise quand on la connait : la faire deviner coute un temps de
            // detection pour un resultat qu on avait deja.
            if (!string.IsNullOrWhiteSpace(language) && language != "auto")
                form.Add(new StringContent(language), "language");

            var startedAt = DateTime.UtcNow;
            using var response = await client.PostAsync("audio/transcriptions", form);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Journalise en AVERTISSEMENT, pas en erreur : la cascade a un repli, ce n est
                // pas encore une panne. Le code compte — 429 (quota) se traite autrement qu un 500.
                _logger.LogWarning("[Voxtral] {Code} — {Body}", (int)response.StatusCode,
                    body.Length > 200 ? body[..200] : body);
                return ApiResponse<string>.ErrorResponse($"Voxtral {(int)response.StatusCode}", (int)response.StatusCode);
            }

            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;

            _logger.LogInformation("[Voxtral] Transcrit en {Seconds:F2} s — {Bytes} octets",
                (DateTime.UtcNow - startedAt).TotalSeconds, audioData.Length);

            return ApiResponse<string>.SuccessResponse(text.Trim());
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