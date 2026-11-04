using Microsoft.Extensions.Logging;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// Cascade de synthese : distant d abord, Kokoro en repli. Meme motif que
/// <see cref="TranscriptionCascade"/> — l ordre vient de l enregistrement, pas d une condition ici.
/// </summary>
public class SpeechCascade : ISpeechSynthesizer
{
    private readonly IReadOnlyList<ISpeechSynthesizer> _providers;
    private readonly ILogger<SpeechCascade> _logger;

    public SpeechCascade(IEnumerable<ISpeechSynthesizer> providers, ILogger<SpeechCascade> logger)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    public bool IsReady => _providers.Any(p => p.IsReady);

    public async Task<byte[]?> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            var providerName = provider.GetType().Name;

            if (!provider.IsReady)
            {
                _logger.LogDebug("[Synthese] {Provider} non pret — suivant", providerName);
                continue;
            }

            // Un tour annule ne doit pas relancer une synthese : Kokoro ne sait pas s arreter, et
            // ce travail orphelin retarderait la reponse suivante.
            ct.ThrowIfCancellationRequested();

            var wav = await provider.SynthesizeAsync(text, ct);
            if (wav is { Length: > 0 })
            {
                if (provider != _providers[0])
                    _logger.LogWarning("[Synthese] REPLI utilise : {Provider}", providerName);
                return wav;
            }

            _logger.LogWarning("[Synthese] {Provider} n a rien produit — suivant", providerName);
        }

        return null;
    }
}
