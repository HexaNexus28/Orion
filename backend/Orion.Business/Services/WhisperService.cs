using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
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

    /// <summary>
    /// Whisper.net s'appuie sur une bibliotheque NATIVE qui n'est pas thread-safe :
    /// deux transcriptions simultanees font tomber le processus en segmentation fault.
    /// Observe le 2026-08-20 des que les tours vocaux sont devenus non bloquants et que
    /// plusieurs prises se sont enchainees. La transcription est de toute facon liee au CPU :
    /// la paralleliser n'apporte rien et coute le processus.
    /// </summary>
    private readonly SemaphoreSlim _transcribeLock = new(1, 1);

    // Langues supportées par Whisper
    public IReadOnlyList<string> SupportedLanguages { get; } = new List<string>
    {
        "fr", "en", "es", "de", "it", "pt", "nl", "pl", "ru", "ja", "zh", "ar", "ko", "hi"
    }.AsReadOnly();

    public bool IsReady => _isInitialized && _whisperFactory != null;

    public WhisperService(ILogger<WhisperService> logger)
    {
        _logger = logger;
        _modelPath = Path.Combine(AppContext.BaseDirectory, "models", "whisper", "ggml-small.bin");
        
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
                _logger.LogInformation("[Whisper] Téléchargement du modèle small...");
                await DownloadModelAsync(GgmlType.Small, _modelPath);
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
    public async Task<ApiResponse<string>> TranscribeAsync(Stream audioStream, string? language = null)
    {
        try
        {
            // Attendre l'initialisation
            if (!IsReady)
            {
                await InitializeAsync();
                if (!IsReady)
                {
                    return ApiResponse<string>.ErrorResponse("Whisper non initialisé", 503);
                }
            }

            await _transcribeLock.WaitAsync();
            try
            {
                return await TranscribeCoreAsync(audioStream, language);
            }
            finally
            {
                _transcribeLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Whisper] Erreur de transcription");
            return ApiResponse<string>.ErrorResponse($"Erreur STT: {ex.Message}", 500);
        }
    }

    /// <summary>Transcription proprement dite — toujours appelee sous verrou.</summary>
    private async Task<ApiResponse<string>> TranscribeCoreAsync(Stream audioStream, string? language)
    {
        {
            using var processor = _whisperFactory!.CreateBuilder()
                // Nombre de fils EXPLICITE. Sans lui, Whisper.net en choisit un par defaut qui
                // ignore la limite du conteneur : trop peu, on laisse des coeurs inutilises ;
                // trop, les fils se disputent un quota CPU deja plafonne et la latence empire.
                // ProcessorCount reflete la limite cgroup depuis .NET 6, donc il fait foi.
                .WithThreads(Math.Max(1, Environment.ProcessorCount))

                // « auto » fait payer une detection de langue AVANT de transcrire. L interface
                // envoie deja « fr » : ne pas s en servir, c est acheter deux fois le meme
                // travail. Le repli reste le francais, langue de l utilisateur.
                .WithLanguage(language ?? "fr")
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

            return ApiResponse<string>.SuccessResponse(transcript);
        }
    }

    /// <summary>
    /// Transcrit des données audio brutes
    /// </summary>
    public async Task<ApiResponse<string>> TranscribeAsync(byte[] audioData, string? language = null)
    {
        using var stream = new MemoryStream(audioData);
        return await TranscribeAsync(stream, language);
    }

    public void Dispose()
    {
        _whisperFactory?.Dispose();
        _initLock.Dispose();
        _transcribeLock.Dispose();
    }
}
