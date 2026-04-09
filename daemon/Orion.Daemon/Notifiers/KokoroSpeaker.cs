using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Orion.Daemon.Core.Interfaces;
using System.Runtime.InteropServices;

namespace Orion.Daemon.Notifiers;

/// <summary>
/// KokoroSpeaker - TTS via Kokoro ONNX
/// 
/// Kokoro = moteur TTS neuronal open source
/// ONNX Runtime = inférence locale rapide
/// 
/// Avantage : voix naturelle, 0 coût API, 100% offline
/// Nécessite : kokoro.onnx + voices.json dans /models
/// </summary>
public class KokoroSpeaker : INotifier
{
    private readonly ILogger _logger;
    private InferenceSession? _session;
    private bool _isAvailable = false;
    private readonly string _modelPath;
    private readonly string _voicesPath;

    public string Name => "KokoroSpeaker";
    public bool IsAvailable => _isAvailable;

    public KokoroSpeaker(ILogger logger)
    {
        _logger = logger;
        _modelPath = Path.Combine(AppContext.BaseDirectory, "Voicemodels", "kokoro.onnx");
        _voicesPath = Path.Combine(AppContext.BaseDirectory, "Voicemodels", "voices.json");
        
        InitializeKokoro();
    }

    private void InitializeKokoro()
    {
        try
        {
            if (!File.Exists(_modelPath))
            {
                _logger.LogWarning("[KokoroSpeaker] Model not found at {Path}. Run: wget https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro.onnx", _modelPath);
                return;
            }

            var sessionOptions = new SessionOptions
            {
                InterOpNumThreads = 4,
                IntraOpNumThreads = 4,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            // GPU si disponible (DirectML sur Windows)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    sessionOptions.AppendExecutionProvider_DML(0);
                    _logger.LogInformation("[KokoroSpeaker] Using DirectML GPU");
                }
                catch
                {
                    _logger.LogInformation("[KokoroSpeaker] Using CPU");
                }
            }

            _session = new InferenceSession(_modelPath, sessionOptions);
            _isAvailable = true;

            _logger.LogInformation("[KokoroSpeaker] Kokoro ONNX initialized");
            _logger.LogInformation("[KokoroSpeaker] Model: {Model}", _modelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KokoroSpeaker] Failed to initialize Kokoro");
            _isAvailable = false;
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
        if (!_isAvailable || _session == null)
        {
            _logger.LogWarning("[KokoroSpeaker] Not available for synthesis");
            return null;
        }

        try
        {
            _logger.LogInformation("[KokoroSpeaker] Synthesizing to WAV: {Preview}",
                text.Length > 40 ? text[..40] + "..." : text);

            var wavBytes = await RunInferenceAsync(text);
            _logger.LogInformation("[KokoroSpeaker] WAV bytes: {Bytes}", wavBytes?.Length ?? 0);
            return wavBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KokoroSpeaker] Failed to synthesize to WAV");
            return null;
        }
    }

    /// <summary>
    /// Lecture locale — pour les notifications proactives daemon
    /// </summary>
    public async Task SpeakAsync(string text)
    {
        if (!_isAvailable || _session == null)
        {
            _logger.LogWarning("[KokoroSpeaker] Not available");
            return;
        }

        try
        {
            _logger.LogInformation("[KokoroSpeaker] Speaking locally: {Preview}",
                text.Length > 30 ? text[..30] + "..." : text);

            var wavBytes = await RunInferenceAsync(text);
            if (wavBytes != null)
                await PlayAudioAsync(wavBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KokoroSpeaker] Failed to speak");
        }
    }

    private async Task<byte[]?> RunInferenceAsync(string text)
    {
        var phonemes = TextToPhonemes(text);
        var phonemeIds = PhonemesToIds(phonemes);
        var voiceId = 0;

        var inputTensor = new DenseTensor<long>(phonemeIds, new[] { phonemeIds.Length, 1 });
        var voiceTensor = new DenseTensor<long>(new long[] { voiceId }, new[] { 1, 1 });
        var speedTensor = new DenseTensor<float>(new float[] { 1.0f }, new[] { 1 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("tokens", inputTensor),
            NamedOnnxValue.CreateFromTensor("voice", voiceTensor),
            NamedOnnxValue.CreateFromTensor("speed", speedTensor)
        };

        var results = _session!.Run(inputs);
        using (results)
        {
            var audioData = results.First().AsTensor<float>().ToArray();
            return await Task.FromResult(ConvertToWav(audioData, 24000));
        }
    }

    /// <summary>
    /// Phonémisation basique (placeholder)
    /// En production: utiliser espeak-ng ou phonemizer
    /// </summary>
    private string TextToPhonemes(string text)
    {
        // Simplification: passer le texte directement
        // Kokoro attend des phonèmes IPA
        // TODO: Intégrer espeak-ng pour vraie phonémisation
        return text.ToLower()
            .Replace("hello", "həˈloʊ")
            .Replace("bonjour", "bɔ̃ʒuʁ")
            .Replace("orion", "ɔˈɹaɪən");
    }

    /// <summary>
    /// Conversion phonèmes → IDs (simplifié)
    /// </summary>
    private long[] PhonemesToIds(string phonemes)
    {
        // Mapping basique: chaque caractère = un ID
        // En production: utiliser le vocabulaire Kokoro officiel
        return phonemes.Select(c => (long)(c % 256)).ToArray();
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
    private byte[] ConvertToWav(float[] audioData, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header WAV
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + audioData.Length * 2); // File size
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16); // Subchunk1Size
        writer.Write((short)1); // AudioFormat (PCM)
        writer.Write((short)1); // NumChannels (mono)
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2); // ByteRate
        writer.Write((short)2); // BlockAlign
        writer.Write((short)16); // BitsPerSample
        writer.Write("data".ToCharArray());
        writer.Write(audioData.Length * 2); // Subchunk2Size

        // Data PCM 16-bit
        foreach (var sample in audioData)
        {
            var pcm = (short)(Math.Clamp(sample, -1f, 1f) * 32767);
            writer.Write(pcm);
        }

        return ms.ToArray();
    }
}
