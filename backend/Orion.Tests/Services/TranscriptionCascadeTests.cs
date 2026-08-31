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
    private sealed class FakeTranscriber : IWhisperService
    {
        private readonly ApiResponse<string> _response;
        public bool WasCalled { get; private set; }
        public bool IsReady { get; }
        public IReadOnlyList<string> SupportedLanguages { get; } = new[] { "fr" };

        public FakeTranscriber(ApiResponse<string> response, bool pret = true) { _response = response; IsReady = pret; }

        public Task<ApiResponse<string>> TranscribeAsync(Stream a, string? l = null) => TranscribeAsync(Array.Empty<byte>(), l);
        public Task<ApiResponse<string>> TranscribeAsync(byte[] a, string? l = null)
        {
            WasCalled = true;
            return Task.FromResult(_response);
        }
    }

    private static TranscriptionCascade Build(params IWhisperService[] f)
        => new(f, NullLogger<TranscriptionCascade>.Instance);

    [Fact]
    public async Task Transcribe_FirstProviderSucceeds_StopsCascade()
    {
        var distant = new FakeTranscriber(ApiResponse<string>.SuccessResponse("bonjour"));
        var local = new FakeTranscriber(ApiResponse<string>.SuccessResponse("bonjour aussi"));

        var r = await Build(distant, local).TranscribeAsync(new byte[] { 1 });

        Assert.Equal("bonjour", r.Data);
        Assert.True(distant.WasCalled);
        Assert.False(local.WasCalled);   // le repli ne doit PAS couter 5 s pour rien
    }

    [Fact]
    public async Task Transcribe_RemoteFails_FallsBackToLocal()
    {
        var distant = new FakeTranscriber(ApiResponse<string>.ErrorResponse("429 quota", 429));
        var local = new FakeTranscriber(ApiResponse<string>.SuccessResponse("transcrit en local"));

        var r = await Build(distant, local).TranscribeAsync(new byte[] { 1 });

        Assert.True(r.Success);
        Assert.Equal("transcrit en local", r.Data);
        Assert.True(local.WasCalled);
    }

    [Fact]
    public async Task Transcribe_ProviderNotReady_SkippedWithoutCall()
    {
        var distant = new FakeTranscriber(ApiResponse<string>.SuccessResponse("jamais"), pret: false);
        var local = new FakeTranscriber(ApiResponse<string>.SuccessResponse("local"));

        var r = await Build(distant, local).TranscribeAsync(new byte[] { 1 });

        Assert.False(distant.WasCalled);
        Assert.Equal("local", r.Data);
    }

    [Fact]
    public async Task Transcribe_EmptyResult_IsSuccessNoFallback()
    {
        // Du silence ou du bruit ambiant : reessayer ailleurs ne fera pas apparaitre de parole.
        // Repartir en repli ferait payer 5 s de Whisper pour confirmer qu il n y a rien a dire.
        var distant = new FakeTranscriber(ApiResponse<string>.SuccessResponse(string.Empty));
        var local = new FakeTranscriber(ApiResponse<string>.SuccessResponse("bruit interprete"));

        var r = await Build(distant, local).TranscribeAsync(new byte[] { 1 });

        Assert.Equal(string.Empty, r.Data);
        Assert.False(local.WasCalled);
    }

    [Fact]
    public async Task Transcribe_AllFail_ReturnsLastFailure()
    {
        var distant = new FakeTranscriber(ApiResponse<string>.ErrorResponse("502", 502));
        var local = new FakeTranscriber(ApiResponse<string>.ErrorResponse("modele absent", 503));

        var r = await Build(distant, local).TranscribeAsync(new byte[] { 1 });

        Assert.False(r.Success);
        Assert.Equal(503, r.StatusCode);
    }
}