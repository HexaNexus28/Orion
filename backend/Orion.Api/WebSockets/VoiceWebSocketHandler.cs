using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orion.Core.DTOs.Requests;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.Services;

namespace Orion.Api.WebSockets;

/// <summary>
/// Full-duplex voice WebSocket handler.
/// 
/// Protocol:
///   Client → Server:
///     - Binary: raw PCM 16kHz mono int16 audio chunks
///     - Text JSON: {"type":"end_audio"}     → finalize current utterance
///     - Text JSON: {"type":"interrupt"}     → cancel current LLM+TTS
///     - Text JSON: {"type":"config","language":"fr","sessionId":"..."}
///   
///   Server → Client:
///     - Text JSON: {"type":"ready"}
///     - Text JSON: {"type":"session","id":"..."}
///     - Text JSON: {"type":"transcript","text":"..."}
///     - Text JSON: {"type":"llm_start"}
///     - Text JSON: {"type":"llm_chunk","text":"..."}
///     - Text JSON: {"type":"llm_done","text":"..."}
///     - Binary: WAV audio chunks (complete WAV files, play immediately)
///     - Text JSON: {"type":"tts_done"}
///     - Text JSON: {"type":"error","message":"..."}
/// </summary>
public class VoiceWebSocketHandler
{
    private readonly ILogger<VoiceWebSocketHandler> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWhisperService _whisperService;

    // State
    private WebSocket? _ws;
    private CancellationTokenSource? _turnCts;
    private string _language = "fr";
    private Guid? _sessionId;
    private readonly List<byte> _audioBuffer = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public VoiceWebSocketHandler(
        ILogger<VoiceWebSocketHandler> logger,
        IServiceScopeFactory scopeFactory,
        IWhisperService whisperService)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _whisperService = whisperService;
    }

    public async Task HandleAsync(WebSocket webSocket, CancellationToken ct)
    {
        _ws = webSocket;
        _logger.LogInformation("[VoiceWS] Client connected");

        await SendJsonAsync(new { type = "ready" }, ct);

        var buffer = new byte[64 * 1024]; // 64KB receive buffer

        try
        {
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("[VoiceWS] Client disconnected");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Audio PCM data
                    HandleAudioData(buffer, result.Count);
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await HandleTextMessageAsync(json, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogInformation("[VoiceWS] Connection closed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VoiceWS] Error in receive loop");
        }
        finally
        {
            CancelCurrentTurn();
            _sendLock.Dispose();
        }
    }

    private void HandleAudioData(byte[] buffer, int count)
    {
        // Accumulate PCM audio chunks
        _audioBuffer.AddRange(buffer.AsSpan(0, count).ToArray());
    }

    private async Task HandleTextMessageAsync(string json, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "config":
                    if (doc.RootElement.TryGetProperty("language", out var lang))
                        _language = lang.GetString() ?? "fr";
                    if (doc.RootElement.TryGetProperty("sessionId", out var sid) && 
                        Guid.TryParse(sid.GetString(), out var parsed))
                        _sessionId = parsed;
                    _logger.LogInformation("[VoiceWS] Config: lang={Lang}, session={Sid}", _language, _sessionId);
                    break;

                case "end_audio":
                    // User stopped speaking — process the accumulated audio
                    await ProcessTurnAsync(ct);
                    break;

                case "interrupt":
                    _logger.LogInformation("[VoiceWS] Interrupt requested");
                    CancelCurrentTurn();
                    _audioBuffer.Clear();
                    break;

                default:
                    _logger.LogWarning("[VoiceWS] Unknown message type: {Type}", type);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[VoiceWS] Invalid JSON: {Json}", json);
        }
    }

    /// <summary>
    /// Process one voice turn: STT → LLM stream → TTS stream
    /// </summary>
    private async Task ProcessTurnAsync(CancellationToken ct)
    {
        if (_audioBuffer.Count < 4000) // Less than ~125ms of audio at 16kHz
        {
            _logger.LogDebug("[VoiceWS] Audio too short ({Bytes} bytes), skipping", _audioBuffer.Count);
            _audioBuffer.Clear();
            return;
        }

        // Create cancellation token for this turn (allows barge-in)
        CancelCurrentTurn();
        _turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var turnToken = _turnCts.Token;

        // Grab audio and clear buffer
        var audioData = _audioBuffer.ToArray();
        _audioBuffer.Clear();

        try
        {
            // ── Step 1: STT ──────────────────────────────────────────
            _logger.LogInformation("[VoiceWS] STT start — {Kb}KB audio", audioData.Length / 1024);

            // Encode PCM to WAV for Whisper
            var wavBytes = EncodePcmToWav(audioData, 16000);
            using var wavStream = new MemoryStream(wavBytes);
            var sttResult = await _whisperService.TranscribeAsync(wavStream, _language);

            if (turnToken.IsCancellationRequested) return;

            if (!sttResult.Success || string.IsNullOrWhiteSpace(sttResult.Data))
            {
                _logger.LogWarning("[VoiceWS] STT failed: {Msg}", sttResult.Message);
                await SendJsonAsync(new { type = "error", message = "STT failed" }, turnToken);
                return;
            }

            var transcript = sttResult.Data.Trim();
            _logger.LogInformation("[VoiceWS] STT: {Text}", transcript);
            await SendJsonAsync(new { type = "transcript", text = transcript }, turnToken);

            if (turnToken.IsCancellationRequested) return;

            // ── Step 2: Prepare session (ApiResponse — catches DB errors) ─────
            using var scope = _scopeFactory.CreateScope();
            var conversationAgent = scope.ServiceProvider.GetRequiredService<IConversationAgent>();
            var voiceNotification = scope.ServiceProvider.GetRequiredService<IVoiceNotificationService>();

            var request = new ChatRequest
            {
                Message = transcript,
                SessionId = _sessionId,
                VoiceMode = true
            };

            var prepResult = await conversationAgent.PrepareStreamAsync(request, turnToken);
            if (!prepResult.Success || prepResult.Data == null)
            {
                _logger.LogError("[VoiceWS] PrepareStream failed: {Msg}", prepResult.Message);
                await SendJsonAsync(new { type = "error", message = prepResult.Message }, turnToken);
                return;
            }

            var streamContext = prepResult.Data;
            _sessionId = streamContext.SessionId;
            _logger.LogInformation("[VoiceWS] Session: {Sid}", _sessionId);

            await SendJsonAsync(new { type = "llm_start" }, turnToken);
            await SendJsonAsync(new { type = "session", id = _sessionId.ToString() }, turnToken);

            if (turnToken.IsCancellationRequested) return;

            // ── Step 3: Stream LLM + TTS (sentence-level pipelining) ──
            var sentenceBuffer = new StringBuilder();
            var fullResponse = new StringBuilder();
            var ttsTasks = new List<Task>(); // Pipeline: TTS runs in parallel with LLM stream
            var isFirstChunk = true;

            await foreach (var chunk in conversationAgent.StreamLLMAsync(streamContext, turnToken))
            {
                if (turnToken.IsCancellationRequested) break;

                fullResponse.Append(chunk);
                sentenceBuffer.Append(chunk);

                // Send text chunk to frontend (for display)
                await SendJsonAsync(new { type = "llm_chunk", text = chunk }, turnToken);

                var sentence = sentenceBuffer.ToString();
                var shouldFlush = false;

                // Flush on strong sentence endings (. ! ? or newline) with min 20 chars
                if (sentence.TrimEnd().Length >= 20 && EndsWithSentence(sentence))
                    shouldFlush = true;
                // Early flush: after 80+ chars without punctuation, flush on weak breaks (, : ;)
                else if (sentence.Length >= 80 && EndsWithWeakBreak(sentence))
                    shouldFlush = true;
                // Force flush: prevent buffer from growing too large (150+ chars)
                else if (sentence.Length >= 150)
                    shouldFlush = true;

                if (shouldFlush)
                {
                    var trimmed = sentence.Trim();
                    if (trimmed.Length >= 3)
                    {
                        // Pipeline: fire TTS without awaiting (but await before sending next)
                        if (ttsTasks.Count > 0) await Task.WhenAll(ttsTasks);
                        ttsTasks.Clear();
                        ttsTasks.Add(SynthesizeAndSendAsync(trimmed, voiceNotification, turnToken));
                        if (isFirstChunk) { await Task.WhenAll(ttsTasks); ttsTasks.Clear(); isFirstChunk = false; }
                    }
                    sentenceBuffer.Clear();
                }
            }

            // Await any pending TTS
            if (ttsTasks.Count > 0) await Task.WhenAll(ttsTasks);

            if (turnToken.IsCancellationRequested) return;

            // Flush remaining text
            var remaining = sentenceBuffer.ToString().Trim();
            if (remaining.Length >= 3)
            {
                await SynthesizeAndSendAsync(remaining, voiceNotification, turnToken);
            }

            await SendJsonAsync(new { type = "llm_done", text = fullResponse.ToString() }, turnToken);
            await SendJsonAsync(new { type = "tts_done" }, turnToken);

            _logger.LogInformation("[VoiceWS] Turn complete — {Chars} chars", fullResponse.Length);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[VoiceWS] Turn cancelled (barge-in)");
            await SendJsonSafeAsync(new { type = "interrupted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VoiceWS] Turn processing error");
            await SendJsonSafeAsync(new { type = "error", message = ex.Message });
        }
    }

    private async Task SynthesizeAndSendAsync(string text, IVoiceNotificationService voiceService, CancellationToken ct)
    {
        try
        {
            var wav = await voiceService.SynthesizeAsync(text, ct);
            if (wav != null && wav.Length > 0)
            {
                await SendBinaryAsync(wav, ct);
                _logger.LogDebug("[VoiceWS] Sent {Kb}KB WAV for: '{Preview}'",
                    wav.Length / 1024, text.Length > 30 ? text[..30] + "..." : text);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VoiceWS] TTS failed for: '{Preview}'",
                text.Length > 30 ? text[..30] + "..." : text);
        }
    }

    private void CancelCurrentTurn()
    {
        try
        {
            _turnCts?.Cancel();
            _turnCts?.Dispose();
            _turnCts = null;
        }
        catch { }
    }

    // ── Send helpers (thread-safe) ───────────────────────────────────────────────

    private async Task SendJsonAsync(object message, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task SendBinaryAsync(byte[] data, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;

        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task SendJsonSafeAsync(object message)
    {
        try
        {
            if (_ws != null && _ws.State == WebSocketState.Open)
                await SendJsonAsync(message, CancellationToken.None);
        }
        catch { }
    }

    // ── Audio helpers ────────────────────────────────────────────────────────────

    private static bool EndsWithSentence(string text)
    {
        var t = text.TrimEnd();
        return t.EndsWith('.') || t.EndsWith('?') || t.EndsWith('!') || t.EndsWith('\n');
    }

    private static bool EndsWithWeakBreak(string text)
    {
        var t = text.TrimEnd();
        return t.EndsWith(',') || t.EndsWith(':') || t.EndsWith(';') || t.EndsWith('—');
    }

    /// <summary>
    /// Encode raw PCM int16 data to WAV format for Whisper
    /// </summary>
    private static byte[] EncodePcmToWav(byte[] pcmData, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        int channels = 1;
        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;

        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + pcmData.Length);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16); // SubChunk1Size
        writer.Write((short)1); // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);
        writer.Write("data".ToCharArray());
        writer.Write(pcmData.Length);
        writer.Write(pcmData);

        return ms.ToArray();
    }
}
