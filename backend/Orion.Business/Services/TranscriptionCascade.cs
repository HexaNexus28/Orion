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
    private readonly IReadOnlyList<IWhisperService> _fournisseurs;
    private readonly ILogger<TranscriptionCascade> _logger;

    public TranscriptionCascade(IEnumerable<IWhisperService> fournisseurs, ILogger<TranscriptionCascade> logger)
    {
        _fournisseurs = fournisseurs.ToList();
        _logger = logger;
    }

    /// <summary>Pret si AU MOINS un fournisseur l est — c est tout l interet du repli.</summary>
    public bool IsReady => _fournisseurs.Any(f => f.IsReady);

    public IReadOnlyList<string> SupportedLanguages =>
        _fournisseurs.SelectMany(f => f.SupportedLanguages).Distinct().ToList();

    public async Task<ApiResponse<string>> TranscribeAsync(Stream audioStream, string? language = null)
    {
        // Materialise UNE fois : un flux ne se relit pas, et le repli doit pouvoir retranscrire
        // exactement le meme audio. Sans ca le second fournisseur recevrait un flux vide.
        using var memoire = new MemoryStream();
        await audioStream.CopyToAsync(memoire);
        return await TranscribeAsync(memoire.ToArray(), language);
    }

    public async Task<ApiResponse<string>> TranscribeAsync(byte[] audioData, string? language = null)
    {
        ApiResponse<string>? dernierEchec = null;

        foreach (var fournisseur in _fournisseurs)
        {
            var nom = fournisseur.GetType().Name;

            if (!fournisseur.IsReady)
            {
                _logger.LogDebug("[Transcription] {Nom} non pret — suivant", nom);
                continue;
            }

            var resultat = await fournisseur.TranscribeAsync(audioData, language);

            // Un succes VIDE n est pas un echec : c est du silence ou du bruit ambiant, et
            // reessayer ailleurs ne produirait pas de parole. Repartir en repli ferait payer
            // cinq secondes de Whisper pour confirmer qu il n y a rien a dire.
            if (resultat.Success)
            {
                if (fournisseur != _fournisseurs[0])
                    _logger.LogWarning("[Transcription] REPLI utilise : {Nom}", nom);
                return resultat;
            }

            dernierEchec = resultat;
            _logger.LogWarning("[Transcription] {Nom} a echoue ({Code}) — fournisseur suivant",
                nom, resultat.StatusCode);
        }

        return dernierEchec ?? ApiResponse<string>.ErrorResponse("Aucun service de transcription disponible", 503);
    }
}