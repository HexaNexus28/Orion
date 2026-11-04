using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// Synthese par Voxtral TTS. Rend le meme format que Kokoro (WAV 24 kHz mono 16 bits) : le
/// navigateur ne voit aucune difference. Voir <see cref="SpeechOptions"/> pour les mesures.
/// </summary>
public class VoxtralSpeechService : ISpeechSynthesizer
{
    public const string HttpClientName = "VoxtralSpeech";

    private readonly IHttpClientFactory _httpFactory;
    private readonly SpeechOptions _options;
    private readonly ILogger<VoxtralSpeechService> _logger;

    public VoxtralSpeechService(
        IHttpClientFactory httpFactory,
        IOptions<SpeechOptions> options,
        ILogger<VoxtralSpeechService> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsReady => _options.Enabled && _options.IsConfigured;

    public async Task<byte[]?> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        if (!IsReady) return null;

        try
        {
            var client = _httpFactory.CreateClient(HttpClientName);

            // Pose ICI, pas a l enregistrement : la cle peut venir de PostConfigure, qui passe
            // apres. Meme piege que VoxtralTranscriptionService.
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            var clock = Stopwatch.StartNew();
            using var response = await client.PostAsJsonAsync("audio/speech", new Dictionary<string, string>
            {
                ["model"] = _options.Model,
                ["input"] = text,
                ["voice_id"] = _options.VoiceId,
                ["response_format"] = "wav"
            }, ct);

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[VoxtralTTS] {Code} — {Body}", (int)response.StatusCode,
                    body.Length > 200 ? body[..200] : body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var wav = Convert.FromBase64String(doc.RootElement.GetProperty("audio_data").GetString() ?? string.Empty);

            _logger.LogInformation("[VoxtralTTS] {Chars} caracteres en {Seconds:F2} s — {Kb} Ko",
                text.Length, clock.Elapsed.TotalSeconds, wav.Length / 1024);

            return wav.Length > 0 ? wav : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Le tour est annule : ce n est pas une panne, et aucun repli ne doit partir.
        }
        catch (Exception ex)
        {
            // Delai depasse compris : HttpClient le signale par une annulation sans lien avec `ct`.
            _logger.LogWarning(ex, "[VoxtralTTS] Echec — repli");
            return null;
        }
    }
}
