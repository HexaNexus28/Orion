using Microsoft.Extensions.Logging.Abstractions;
using Orion.Business.Services;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;

namespace Orion.Tests.Services;

/// <summary>
/// La cascade de transcription. Un repli qui ne se declenche pas ne sert a rien, et un repli qui
/// se declenche trop fait payer 5 s de Whisper local pour rien.
/// </summary>
public class TranscriptionCascadeTests
{
    private sealed class Faux : IWhisperService
    {
        private readonly ApiResponse<string> _reponse;
        public bool Appele { get; private set; }
        public bool IsReady { get; }
        public IReadOnlyList<string> SupportedLanguages { get; } = new[] { "fr" };

        public Faux(ApiResponse<string> reponse, bool pret = true) { _reponse = reponse; IsReady = pret; }

        public Task<ApiResponse<string>> TranscribeAsync(Stream a, string? l = null) => TranscribeAsync(Array.Empty<byte>(), l);
        public Task<ApiResponse<string>> TranscribeAsync(byte[] a, string? l = null)
        {
            Appele = true;
            return Task.FromResult(_reponse);
        }
    }

    private static TranscriptionCascade Construire(params IWhisperService[] f)
        => new(f, NullLogger<TranscriptionCascade>.Instance);

    [Fact]
    public async Task Le_premier_fournisseur_qui_reussit_arrete_la_cascade()
    {
        var distant = new Faux(ApiResponse<string>.SuccessResponse("bonjour"));
        var local = new Faux(ApiResponse<string>.SuccessResponse("bonjour aussi"));

        var r = await Construire(distant, local).TranscribeAsync(new byte[] { 1 });

        Assert.Equal("bonjour", r.Data);
        Assert.True(distant.Appele);
        Assert.False(local.Appele);   // le repli ne doit PAS couter 5 s pour rien
    }

    [Fact]
    public async Task Un_echec_distant_declenche_le_repli_local()
    {
        var distant = new Faux(ApiResponse<string>.ErrorResponse("429 quota", 429));
        var local = new Faux(ApiResponse<string>.SuccessResponse("transcrit en local"));

        var r = await Construire(distant, local).TranscribeAsync(new byte[] { 1 });

        Assert.True(r.Success);
        Assert.Equal("transcrit en local", r.Data);
        Assert.True(local.Appele);
    }

    [Fact]
    public async Task Un_fournisseur_NON_pret_est_saute_sans_etre_appele()
    {
        var distant = new Faux(ApiResponse<string>.SuccessResponse("jamais"), pret: false);
        var local = new Faux(ApiResponse<string>.SuccessResponse("local"));

        var r = await Construire(distant, local).TranscribeAsync(new byte[] { 1 });

        Assert.False(distant.Appele);
        Assert.Equal("local", r.Data);
    }

    [Fact]
    public async Task Une_transcription_VIDE_est_un_succes_et_ne_declenche_PAS_le_repli()
    {
        // Du silence ou du bruit ambiant : reessayer ailleurs ne fera pas apparaitre de parole.
        // Repartir en repli ferait payer 5 s de Whisper pour confirmer qu il n y a rien a dire.
        var distant = new Faux(ApiResponse<string>.SuccessResponse(string.Empty));
        var local = new Faux(ApiResponse<string>.SuccessResponse("bruit interprete"));

        var r = await Construire(distant, local).TranscribeAsync(new byte[] { 1 });

        Assert.Equal(string.Empty, r.Data);
        Assert.False(local.Appele);
    }

    [Fact]
    public async Task Tous_en_echec_rend_le_dernier_echec_et_non_un_succes_vide()
    {
        var distant = new Faux(ApiResponse<string>.ErrorResponse("502", 502));
        var local = new Faux(ApiResponse<string>.ErrorResponse("modele absent", 503));

        var r = await Construire(distant, local).TranscribeAsync(new byte[] { 1 });

        Assert.False(r.Success);
        Assert.Equal(503, r.StatusCode);
    }
}