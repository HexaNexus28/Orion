using System.Text;
using Microsoft.AspNetCore.Mvc;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.Services;

namespace Orion.Api.Controllers;

/// <summary>
/// VoiceController - STT (Whisper) + TTS (Kokoro via daemon)
/// Phase 4 : Voix temps réel
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class VoiceController : ControllerBase
{
    private readonly IWhisperService _whisperService;
    private readonly IVoiceNotificationService _voiceNotification;
    private readonly IConversationAgent _conversationAgent;
    private readonly ILogger<VoiceController> _logger;

    public VoiceController(
        IWhisperService whisperService,
        IVoiceNotificationService voiceNotification,
        IConversationAgent conversationAgent,
        ILogger<VoiceController> logger)
    {
        _whisperService = whisperService;
        _voiceNotification = voiceNotification;
        _conversationAgent = conversationAgent;
        _logger = logger;
    }

    /// <summary>
    /// Synthèse vocale via Kokoro (daemon) — retourne WAV bytes pour AudioContext frontend.
    /// 503 si daemon déconnecté ou Kokoro indisponible → frontend utilise Web Speech API.
    /// </summary>
    [HttpPost("synthesize")]
    [Consumes("application/json")]
    public async Task<IActionResult> Synthesize([FromBody] SynthesizeRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.Text))
            return BadRequest(new { error = "Text is required" });

        _logger.LogInformation("[Voice/TTS] Synthesis request: {Preview}",
            request.Text.Length > 50 ? request.Text[..50] + "..." : request.Text);

        var wavBytes = await _voiceNotification.SynthesizeAsync(request.Text, ct);

        if (wavBytes == null || wavBytes.Length == 0)
        {
            // Daemon déconnecté ou Kokoro indisponible — frontend doit utiliser fallback
            _logger.LogWarning("[Voice/TTS] Synthesis unavailable, returning 503 for frontend fallback");
            return StatusCode(503, new { error = "TTS unavailable", fallback = true });
        }

        _logger.LogInformation("[Voice/TTS] Returning {Kb}KB WAV", wavBytes.Length / 1024);
        return File(wavBytes, "audio/wav");
    }

    /// <summary>
    /// Transcrit un fichier audio en texte (format multipart pour upload direct)
    /// </summary>
    /// <param name="audioFile">Fichier audio (WebM, WAV, MP3, etc.)</param>
    /// <param name="language">Langue optionnelle (fr, en, etc.)</param>
    /// <returns>Texte transcrit</returns>
    [HttpPost("transcribe")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<VoiceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Transcribe(IFormFile audioFile, [FromQuery] string? language = null)
    {
        if (audioFile == null || audioFile.Length == 0)
        {
            return BadRequest(ApiResponse<object>.ErrorResponse("Fichier audio requis"));
        }

        // Validation du type MIME
        var allowedTypes = new[] { "audio/webm", "audio/wav", "audio/mpeg", "audio/mp3", "audio/ogg", "audio/opus" };
        if (!allowedTypes.Contains(audioFile.ContentType) && !audioFile.ContentType.StartsWith("audio/"))
        {
            return BadRequest(ApiResponse<object>.ErrorResponse($"Type audio non supporté: {audioFile.ContentType}"));
        }

        try
        {
            _logger.LogInformation("[Voice] Transcription demandée - {Size} bytes, Langue: {Language}", 
                audioFile.Length, language ?? "auto");

            using var stream = audioFile.OpenReadStream();
            var result = await _whisperService.TranscribeAsync(stream, language);

            if (result.Success && result.Data != null)
            {
                var response = new VoiceResponse
                {
                    Transcript = result.Data,
                    Confidence = 0.95, // Whisper ne fournit pas de confidence score natif
                    Language = language ?? "auto"
                };

                _logger.LogInformation("[Voice] Transcription réussie - {Length} caractères", result.Data.Length);
                return Ok(ApiResponse<VoiceResponse>.SuccessResponse(response, "Transcription réussie"));
            }

            return BadRequest(ApiResponse<object>.ErrorResponse(result.Message ?? "Échec de la transcription"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Voice] Erreur lors de la transcription");
            return StatusCode(500, ApiResponse<object>.ErrorResponse("Erreur interne lors de la transcription"));
        }
    }

    /// <summary>
    /// Transcrit un audio encodé en base64 (format JSON pour frontend)
    /// </summary>
    /// <param name="request">Audio en base64 + type MIME</param>
    /// <returns>Texte transcrit</returns>
    [HttpPost("transcribe/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ApiResponse<VoiceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TranscribeJson([FromBody] VoiceRequest request)
    {
        if (string.IsNullOrEmpty(request?.AudioBase64))
        {
            return BadRequest(ApiResponse<object>.ErrorResponse("AudioBase64 requis"));
        }

        try
        {
            _logger.LogInformation("[Voice] Transcription JSON demandée - {Length} caractères base64, Langue: {Language}", 
                request.AudioBase64.Length, request.Language ?? "auto");

            // Décoder base64
            byte[] audioBytes;
            try
            {
                audioBytes = Convert.FromBase64String(request.AudioBase64);
            }
            catch (FormatException)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse("Format base64 invalide"));
            }

            // Transcrire
            var result = await _whisperService.TranscribeAsync(audioBytes, request.Language);

            if (result.Success && result.Data != null)
            {
                var response = new VoiceResponse
                {
                    Transcript = result.Data,
                    Confidence = 0.95,
                    Language = request.Language ?? "auto"
                };

                _logger.LogInformation("[Voice] Transcription JSON réussie - {Length} caractères", result.Data.Length);
                return Ok(ApiResponse<VoiceResponse>.SuccessResponse(response, "Transcription réussie"));
            }

            return BadRequest(ApiResponse<object>.ErrorResponse(result.Message ?? "Échec de la transcription"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Voice] Erreur lors de la transcription JSON");
            return StatusCode(500, ApiResponse<object>.ErrorResponse("Erreur interne lors de la transcription"));
        }
    }

    /// <summary>
    /// Pipeline voix complet en streaming bout-en-bout :
    /// Audio → Whisper STT → LLM stream → Kokoro TTS chunk par chunk → Audio stream
    /// Latence perçue : ~800ms au lieu de 4-8s
    /// Un seul appel réseau au lieu de 3
    /// </summary>
    [HttpPost("converse")]
    [Consumes("multipart/form-data")]
    public async Task Converse(
        IFormFile audioFile,
        [FromQuery] string? language = "fr",
        [FromQuery] string? sessionId = null,
        CancellationToken ct = default)
    {
        Response.ContentType = "audio/wav";
        Response.Headers["X-Accel-Buffering"] = "no";
        

        if (audioFile == null || audioFile.Length == 0)
        {
            Response.StatusCode = 400;
            return;
        }

        try
        {
            // ── Étape 1 : STT ──────────────────────────────────────────────
            _logger.LogInformation("[Voice/Converse] STT start — {Size} bytes", audioFile.Length);
            using var audioStream = audioFile.OpenReadStream();
            var sttResult = await _whisperService.TranscribeAsync(audioStream, language);

            if (!sttResult.Success || string.IsNullOrEmpty(sttResult.Data))
            {
                _logger.LogWarning("[Voice/Converse] STT failed: {Error}", sttResult.Message);
                Response.StatusCode = 400;
                return;
            }

            var transcript = sttResult.Data;
            _logger.LogInformation("[Voice/Converse] STT done: {Text}", transcript);

            // Envoie le transcript URL-encodé dans le header (HTTP headers = ASCII only)
            Response.Headers["X-Transcript"] = Uri.EscapeDataString(transcript);

            // ── Étape 2 : LLM stream + TTS phrase par phrase ───────────────
            var buffer = new StringBuilder();
            var fullResponse = new StringBuilder();
            var audioFlushed = false;
            var request = new ChatRequest
            {
                Message = transcript,
                SessionId = sessionId != null && Guid.TryParse(sessionId, out var sid) ? sid : null
            };

            await foreach (var evt in _conversationAgent.StreamAsync(request, ct))
            {
                // Seuls les tokens alimentent la synthèse vocale ; les événements d'outils
                // sont du signal pour l'UI, pas du texte à lire à voix haute.
                if (evt.Type != AgentEventType.Token || string.IsNullOrEmpty(evt.Text)) continue;

                var chunk = evt.Text;
                buffer.Append(chunk);
                fullResponse.Append(chunk);

                // Dès qu'une phrase est complète → synthétise et envoie immédiatement
                if (EndsWithSentence(buffer.ToString()))
                {
                    if (await TrySynthesizeAndFlushAsync(buffer.ToString().Trim(), ct))
                        audioFlushed = true;
                    buffer.Clear();
                }
            }

            // Flush le reste du buffer (dernière phrase sans ponctuation finale)
            if (buffer.Length > 0)
            {
                if (await TrySynthesizeAndFlushAsync(buffer.ToString().Trim(), ct))
                    audioFlushed = true;
            }

            // Fallback : si aucun audio n'a été produit (Kokoro down), envoyer le texte
            if (!audioFlushed && fullResponse.Length > 0)
            {
                _logger.LogWarning("[Voice/Converse] No audio produced — sending text fallback");
                Response.ContentType = "text/plain; charset=utf-8";
                var textBytes = Encoding.UTF8.GetBytes(fullResponse.ToString());
                await Response.Body.WriteAsync(textBytes, ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[Voice/Converse] Cancelled by client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Voice/Converse] Error");
            if (!Response.HasStarted)
                Response.StatusCode = 500;
        }
    }

    // ── Helpers privés ──────────────────────────────────────────────────────────

    private static bool EndsWithSentence(string text)
    {
        var t = text.TrimEnd();
        return t.EndsWith('.') || t.EndsWith('?') || t.EndsWith('!') || t.EndsWith('\n');
    }

    private async Task<bool> TrySynthesizeAndFlushAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            var wav = await _voiceNotification.SynthesizeAsync(text, ct);
            if (wav != null && wav.Length > 0)
            {
                await Response.Body.WriteAsync(wav, ct);
                await Response.Body.FlushAsync(ct);

                _logger.LogDebug("[Voice/Converse] Chunk flushed: '{Preview}' → {Kb}KB",
                    text.Length > 30 ? text[..30] + "..." : text,
                    wav.Length / 1024);
                return true;
            }

            _logger.LogWarning("[Voice/Converse] No audio for: '{Preview}' (Kokoro unavailable?)",
                text.Length > 30 ? text[..30] + "..." : text);
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Voice/Converse] TTS timeout for: '{Preview}'",
                text.Length > 30 ? text[..30] + "..." : text);
            return false;
        }
    }

    /// <summary>
    /// Vérifie le statut du service Whisper
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<VoiceStatusResponse>), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var response = new VoiceStatusResponse
        {
            IsReady = _whisperService.IsReady,
            SupportedLanguages = _whisperService.SupportedLanguages.ToList()
        };

        return Ok(ApiResponse<VoiceStatusResponse>.SuccessResponse(response));
    }
}

