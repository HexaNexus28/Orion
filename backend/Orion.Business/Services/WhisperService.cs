using Microsoft.Extensions.Logging;
using Orion.Core.Common;
using Orion.Core.Interfaces.Services;
using Whisper.net;
using Whisper.net.Ggml;

namespace Orion.Business.Services;

/// <summary>
/// WhisperService - Transcription STT locale via Whisper.net
/// 
/// Avantages : 100% offline, 0 coût API, latence faible
/// Nécessite : modèle GGML dans /models/whisper/
/// 
/// Phase 4 : Reconnaissance vocale temps réel pour ORION
/// </summary>
public class WhisperService : IWhisperService, IDisposable
{
    private readonly ILogger _logger;
    private WhisperFactory? _whisperFactory;
    private readonly string _modelPath;
    private bool _isInitialized = false;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // Langues supportées par Whisper
    public IReadOnlyList<string> SupportedLanguages { get; } = new List<string>
    {
        "fr", "en", "es", "de", "it", "pt", "nl", "pl", "ru", "ja", "zh", "ar", "ko", "hi"
    }.AsReadOnly();

    public bool IsReady => _isInitialized && _whisperFactory != null;

    public WhisperService(ILogger<WhisperService> logger)
    {
        _logger = logger;
        _modelPath = Path.Combine(AppContext.BaseDirectory, "models", "whisper", "ggml-base.bin");
        
        // Initialisation lazy - ne bloque pas le constructeur
        _ = InitializeAsync();
    }

    /// <summary>
    /// Télécharge le modèle si nécessaire et initialise Whisper
    /// </summary>
    private async Task InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            // Créer le dossier models si nécessaire
            var modelDir = Path.GetDirectoryName(_modelPath);
            if (!string.IsNullOrEmpty(modelDir) && !Directory.Exists(modelDir))
            {
                Directory.CreateDirectory(modelDir);
            }

            // Télécharger le modèle s'il n'existe pas
            if (!File.Exists(_modelPath))
            {
                _logger.LogInformation("[Whisper] Téléchargement du modèle base...");
                await DownloadModelAsync(GgmlType.Base, _modelPath);
                _logger.LogInformation("[Whisper] Modèle téléchargé avec succès");
            }

            // Initialiser Whisper
            _logger.LogInformation("[Whisper] Chargement du modèle...");
            _whisperFactory = WhisperFactory.FromPath(_modelPath);
            _isInitialized = true;
            
            _logger.LogInformation("[Whisper] Service prêt - Modèle: {Model}", Path.GetFileName(_modelPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Whisper] Échec de l'initialisation");
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Télécharge un modèle Whisper depuis HuggingFace
    /// </summary>
    private async Task DownloadModelAsync(GgmlType type, string outputPath)
    {
        using var httpClient = new HttpClient();
        var url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{type.ToString().ToLower()}.bin";
        
        _logger.LogInformation("[Whisper] Download from: {Url}", url);
        
        await using var modelStream = await httpClient.GetStreamAsync(url);
        await using var fileStream = File.Create(outputPath);
        await modelStream.CopyToAsync(fileStream);
    }

    /// <summary>
    /// Transcrit un stream audio
    /// </summary>
    public async Task<Result<string>> TranscribeAsync(Stream audioStream, string? language = null)
    {
        try
        {
            // Attendre l'initialisation
            if (!IsReady)
            {
                await InitializeAsync();
                if (!IsReady)
                {
                    return Result<string>.Failure("Whisper non initialisé");
                }
            }

            using var processor = _whisperFactory!.CreateBuilder()
                .WithLanguage(language ?? "auto")
                .Build();

            // Whisper attend du audio WAV 16kHz mono
            // Le frontend envoie du WebM/Opus - on pourrait convertir ici si nécessaire
            // Pour l'instant, on assume que l'input est compatible ou converti côté frontend

            var text = new System.Text.StringBuilder();
            
            await foreach (var result in processor.ProcessAsync(audioStream))
            {
                text.Append(result.Text);
            }

            var transcript = text.ToString().Trim();
            _logger.LogDebug("[Whisper] Transcrit: {Length} caractères", transcript.Length);
            
            return Result<string>.Success(transcript);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Whisper] Erreur de transcription");
            return Result<string>.Failure($"Erreur STT: {ex.Message}");
        }
    }

    /// <summary>
    /// Transcrit des données audio brutes
    /// </summary>
    public async Task<Result<string>> TranscribeAsync(byte[] audioData, string? language = null)
    {
        using var stream = new MemoryStream(audioData);
        return await TranscribeAsync(stream, language);
    }

    public void Dispose()
    {
        _whisperFactory?.Dispose();
        _initLock.Dispose();
    }
}
