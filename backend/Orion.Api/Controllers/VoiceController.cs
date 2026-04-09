using Microsoft.AspNetCore.Mvc;
using Orion.Business.Services;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
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
    private readonly VoiceNotificationService _voiceNotification;
    private readonly ILogger<VoiceController> _logger;

    public VoiceController(
        IWhisperService whisperService,
        VoiceNotificationService voiceNotification,
        ILogger<VoiceController> logger)
    {
        _whisperService = whisperService;
        _voiceNotification = voiceNotification;
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

            if (result.IsSuccess && result.Value != null)
            {
                var response = new VoiceResponse
                {
                    Transcript = result.Value,
                    Confidence = 0.95, // Whisper ne fournit pas de confidence score natif
                    Language = language ?? "auto"
                };

                _logger.LogInformation("[Voice] Transcription réussie - {Length} caractères", result.Value.Length);
                return Ok(ApiResponse<VoiceResponse>.SuccessResponse(response, "Transcription réussie"));
            }

            return BadRequest(ApiResponse<object>.ErrorResponse(result.Error ?? "Échec de la transcription"));
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

            if (result.IsSuccess && result.Value != null)
            {
                var response = new VoiceResponse
                {
                    Transcript = result.Value,
                    Confidence = 0.95,
                    Language = request.Language ?? "auto"
                };

                _logger.LogInformation("[Voice] Transcription JSON réussie - {Length} caractères", result.Value.Length);
                return Ok(ApiResponse<VoiceResponse>.SuccessResponse(response, "Transcription réussie"));
            }

            return BadRequest(ApiResponse<object>.ErrorResponse(result.Error ?? "Échec de la transcription"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Voice] Erreur lors de la transcription JSON");
            return StatusCode(500, ApiResponse<object>.ErrorResponse("Erreur interne lors de la transcription"));
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

