using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// Cascade de transcription : distant d abord, local en repli.
///
/// Meme motif que <c>LLMCascade</c> pour le cerveau — un fournisseur rapide en tete, un repli qui
/// ne depend de personne. La voix continue de fonctionner si Mistral tombe, change ses conditions
/// ou refuse une requete : plus lentement, mais elle fonctionne.
///
/// L ordre vient de l ENREGISTREMENT, pas d une condition ecrite ici : ajouter un fournisseur
/// revient a l inserer dans la liste, sans toucher a cette classe.
/// </summary>
public class TranscriptionCascade : IWhisperService
{
    private readonly IReadOnlyList<IWhisperService> _providers;
    private readonly ILogger<TranscriptionCascade> _logger;

    public TranscriptionCascade(IEnumerable<IWhisperService> providers, ILogger<TranscriptionCascade> logger)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    /// <summary>Pret si AU MOINS un fournisseur l est — c est tout l interet du repli.</summary>
    public bool IsReady => _providers.Any(f => f.IsReady);

    public IReadOnlyList<string> SupportedLanguages =>
        _providers.SelectMany(f => f.SupportedLanguages).Distinct().ToList();

    public async Task<ApiResponse<string>> TranscribeAsync(Stream audioStream, string? language = null)
    {
        // Materialise UNE fois : un flux ne se relit pas, et le repli doit pouvoir retranscrire
        // exactement le meme audio. Sans ca le second fournisseur recevrait un flux vide.
        using var buffer = new MemoryStream();
        await audioStream.CopyToAsync(buffer);
        return await TranscribeAsync(buffer.ToArray(), language);
    }

    public async Task<ApiResponse<string>> TranscribeAsync(byte[] audioData, string? language = null)
    {
        ApiResponse<string>? lastFailure = null;

        foreach (var provider in _providers)
        {
            var providerName = provider.GetType().Name;

            if (!provider.IsReady)
            {
                _logger.LogDebug("[Transcription] {Provider} non pret — suivant", providerName);
                continue;
            }

            var result = await provider.TranscribeAsync(audioData, language);

            // Un succes VIDE n est pas un echec : c est du silence ou du bruit ambiant, et
            // reessayer ailleurs ne produirait pas de parole. Repartir en repli ferait payer
            // cinq secondes de Whisper pour confirmer qu il n y a rien a dire.
            if (result.Success)
            {
                if (provider != _providers[0])
                    _logger.LogWarning("[Transcription] REPLI utilise : {Provider}", providerName);
                return result;
            }

            lastFailure = result;
            _logger.LogWarning("[Transcription] {Provider} a echoue ({Code}) — provider suivant",
                providerName, result.StatusCode);
        }

        return lastFailure ?? ApiResponse<string>.ErrorResponse("Aucun service de transcription disponible", 503);
    }
}