namespace Orion.Core.Interfaces.Services;

/// <summary>Transforme du texte en WAV. Plusieurs fournisseurs, une cascade devant eux.</summary>
public interface ISpeechSynthesizer
{
    bool IsReady { get; }

    /// <summary>Retourne le WAV, ou null si ce fournisseur n'a rien pu produire.</summary>
    Task<byte[]?> SynthesizeAsync(string text, CancellationToken ct = default);
}
