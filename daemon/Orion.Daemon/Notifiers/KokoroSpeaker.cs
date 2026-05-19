using Microsoft.Extensions.Logging;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using Orion.Daemon.Core.Interfaces;
using System.Runtime.InteropServices;

namespace Orion.Daemon.Notifiers;

/// <summary>
/// KokoroSpeaker - TTS via KokoroSharp (Kokoro ONNX + espeak-ng phonémisation)
/// 
/// Voix naturelle, 0 coût API, 100% offline
/// Supporte le français nativement via espeak-ng intégré
/// </summary>
public class KokoroSpeaker : INotifier
{
    private readonly ILogger _logger;
    private KokoroTTS? _tts;
    private KokoroVoice? _voice;
    private bool _isAvailable;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitializing;

    private const int SampleRate = 24000;
    private const float SpeechSpeed = 1.0f;

    public string Name => "KokoroSpeaker";
    public bool IsAvailable => _isAvailable;

    public KokoroSpeaker(ILogger logger)
    {
        _logger = logger;
        _ = Task.Run(InitializeAsync);
    }

    private async Task InitializeAsync()
    {
        if (_isInitializing) return;
        await _initLock.WaitAsync();
        try
        {
            if (_isAvailable) return;
            _isInitializing = true;

            _logger.LogInformation("[KokoroSpeaker] Loading KokoroSharp model (may download ~320MB on first run)...");

            // LoadModel auto-télécharge si le modèle n'est pas présent
            _tts = await Task.Run(() => KokoroTTS.LoadModel());

            // Lister les voix disponibles
            var voices = KokoroVoiceManager.Voices.ToList();
            _logger.LogInformation("[KokoroSpeaker] Available voices: {Voices}",
                string.Join(", ", voices.Select(v => v.Name)));

            // Voix française — ff_siwis (French female)
            _voice = KokoroVoiceManager.GetVoice("ff_siwis");
            if (_voice == null)
            {
                _logger.LogWarning("[KokoroSpeaker] Voice 'ff_siwis' not found, trying fallbacks...");
                // Fallback : toute voix française (préfixe ff_) ou af_heart
                _voice = voices.FirstOrDefault(v => v.Name.StartsWith("ff_"))
                      ?? KokoroVoiceManager.GetVoice("af_heart");
            }

            if (_voice == null)
            {
                _logger.LogError("[KokoroSpeaker] No suitable voice found");
                return;
            }

            _isAvailable = true;
            _logger.LogInformation("[KokoroSpeaker] Ready — Voice: {Voice}", _voice.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KokoroSpeaker] Failed to initialize KokoroSharp");
            _isAvailable = false;
        }
        finally
        {
            _isInitializing = false;
            _initLock.Release();
        }
    }

    public Task NotifyAsync(string title, string message, NotificationPriority priority = NotificationPriority.Normal)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Synthèse vers WAV bytes — pour streaming vers le frontend via AudioContext
    /// </summary>
    public async Task<byte[]?> SynthesizeToWavAsync(string text)
    {
        if (!_isAvailable || _tts == null || _voice == null)
        {
            _logger.LogWarning("[KokoroSpeaker] Not available for synthesis");
            return null;
        }

        try
        {
            _logger.LogInformation("[KokoroSpeaker] Synthesizing: {Preview}",
                text.Length > 40 ? text[..40] + "..." : text);

            var allSamples = new List<float>();
            var tcs = new TaskCompletionSource<bool>();

            int[] tokens = Tokenizer.Tokenize(text);

            if (tokens.Length == 0)
            {
                _logger.LogWarning("[KokoroSpeaker] Tokenization returned empty tokens");
                return null;
            }

            var segments = SegmentationSystem.SplitToSegments(tokens, new DefaultSegmentationConfig());
            int remaining = segments.Count;

            var job = KokoroJob.Create(segments, _voice, SpeechSpeed, samples =>
            {
                lock (allSamples)
                {
                    allSamples.AddRange(samples);
                    remaining--;
                    if (remaining <= 0)
                        tcs.TrySetResult(true);
                }
            });

            _tts.EnqueueJob(job);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            cts.Token.Register(() => tcs.TrySetCanceled());

            await tcs.Task;

            if (allSamples.Count == 0)
            {
                _logger.LogWarning("[KokoroSpeaker] No audio generated");
                return null;
            }

            var wavBytes = ConvertToWav(allSamples.ToArray(), SampleRate);
            _logger.LogInformation("[KokoroSpeaker] WAV: {Kb}KB ({Samples} samples)",
                wavBytes.Length / 1024, allSamples.Count);
            return wavBytes;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[KokoroSpeaker] Synthesis timeout");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KokoroSpeaker] Synthesis failed");
            return null;
        }
    }

    /// <summary>
    /// Stream synthesis — yields WAV bytes per segment for lower latency.
    /// Each segment (~1-2 sentences) is returned as soon as ready.
    /// </summary>
    public async IAsyncEnumerable<byte[]> SynthesizeStreamAsync(string text)
    {
        if (!_isAvailable || _tts == null || _voice == null)
        {
            _logger.LogWarning("[KokoroSpeaker] Not available for streaming synthesis");
            yield break;
        }

        int[] tokens = Tokenizer.Tokenize(text);
        if (tokens.Length == 0) yield break;

        var segments = SegmentationSystem.SplitToSegments(tokens, new DefaultSegmentationConfig());
        var channel = System.Threading.Channels.Channel.CreateUnbounded<float[]>();
        int remaining = segments.Count;

        var job = KokoroJob.Create(segments, _voice, SpeechSpeed, samples =>
        {
            channel.Writer.TryWrite(samples);
            if (Interlocked.Decrement(ref remaining) <= 0)
                channel.Writer.TryComplete();
        });

        _tts.EnqueueJob(job);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await foreach (var samples in channel.Reader.ReadAllAsync(cts.Token))
        {
            if (samples.Length > 0)
                yield return ConvertToWav(samples, SampleRate);
        }
    }

    /// <summary>
    /// Lecture locale — pour les notifications proactives daemon
    /// </summary>
    public async Task SpeakAsync(string text)
    {
        if (!_isAvailable || _tts == null || _voice == null)
        {
            _logger.LogWarning("[KokoroSpeaker] Not available");
            return;
        }

        try
        {
            _logger.LogInformation("[KokoroSpeaker] Speaking: {Preview}",
                text.Length > 30 ? text[..30] + "..." : text);

            var wavBytes = await SynthesizeToWavAsync(text);
            if (wavBytes != null)
                await PlayAudioAsync(wavBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KokoroSpeaker] Failed to speak");
        }
    }

    /// <summary>
    /// Joue les WAV bytes localement (notifs proactives daemon)
    /// </summary>
    private async Task PlayAudioAsync(byte[] wavBytes)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"kokoro_{Guid.NewGuid()}.wav");
            await File.WriteAllBytesAsync(tempPath, wavBytes);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var player = new System.Media.SoundPlayer(tempPath);
                player.PlaySync();
            }

            try { File.Delete(tempPath); } catch { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KokoroSpeaker] Failed to play audio");
        }
    }

    /// <summary>
    /// Convertit float[] audio en WAV PCM 16-bit
    /// </summary>
    private static byte[] ConvertToWav(float[] audioData, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + audioData.Length * 2);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data".ToCharArray());
        writer.Write(audioData.Length * 2);

        foreach (var sample in audioData)
        {
            var pcm = (short)(Math.Clamp(sample, -1f, 1f) * 32767);
            writer.Write(pcm);
        }

        return ms.ToArray();
    }
}
