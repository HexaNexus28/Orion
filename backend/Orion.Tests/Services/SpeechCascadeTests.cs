using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Orion.Business.Services;
using Orion.Core.Configuration;
using Orion.Core.Interfaces.Services;

namespace Orion.Tests.Services;

/// <summary>
/// La voix d ORION. Un repli qui ne part pas rend ORION muet ; un repli qui part apres une
/// annulation occupe Kokoro, qui ne sait pas s arreter, et retarde la reponse suivante.
/// </summary>
public class SpeechCascadeTests
{
    private static readonly byte[] Wav = { 1, 2, 3 };

    private sealed class FakeSpeech : ISpeechSynthesizer
    {
        private readonly Func<CancellationToken, byte[]?> _synthesize;
        public int Calls { get; private set; }
        public bool IsReady { get; }

        public FakeSpeech(byte[]? wav, bool ready = true) : this(_ => wav, ready) { }

        public FakeSpeech(Func<CancellationToken, byte[]?> synthesize, bool ready = true)
        {
            _synthesize = synthesize;
            IsReady = ready;
        }

        public Task<byte[]?> SynthesizeAsync(string text, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_synthesize(ct));
        }
    }

    private static SpeechCascade Cascade(params ISpeechSynthesizer[] providers) =>
        new(providers, NullLogger<SpeechCascade>.Instance);

    [Fact]
    public async Task First_Provider_Answers_Fallback_Stays_Idle()
    {
        var fallback = new FakeSpeech(new byte[] { 9 });

        var wav = await Cascade(new FakeSpeech(Wav), fallback).SynthesizeAsync("bonjour");

        Assert.Equal(Wav, wav);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Failed_Provider_Falls_Back()
    {
        var wav = await Cascade(new FakeSpeech((byte[]?)null), new FakeSpeech(Wav)).SynthesizeAsync("bonjour");

        Assert.Equal(Wav, wav);
    }

    [Fact]
    public async Task Empty_Audio_Counts_As_Failure()
    {
        var wav = await Cascade(new FakeSpeech(Array.Empty<byte>()), new FakeSpeech(Wav)).SynthesizeAsync("bonjour");

        Assert.Equal(Wav, wav);
    }

    [Fact]
    public async Task Provider_Not_Ready_Is_Skipped()
    {
        var unready = new FakeSpeech(new byte[] { 9 }, ready: false);

        var wav = await Cascade(unready, new FakeSpeech(Wav)).SynthesizeAsync("bonjour");

        Assert.Equal(Wav, wav);
        Assert.Equal(0, unready.Calls);
    }

    [Fact]
    public async Task All_Failed_Returns_Null()
    {
        Assert.Null(await Cascade(new FakeSpeech((byte[]?)null)).SynthesizeAsync("bonjour"));
    }

    [Fact]
    public async Task Cancelled_Turn_Never_Reaches_The_Fallback()
    {
        using var cts = new CancellationTokenSource();
        var first = new FakeSpeech(_ => { cts.Cancel(); return null; });
        var fallback = new FakeSpeech(Wav);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Cascade(first, fallback).SynthesizeAsync("bonjour", cts.Token));

        Assert.Equal(0, fallback.Calls);
    }

    // ── VoxtralSpeechService ─────────────────────────────────────────────────

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;
        public string? Body { get; private set; }
        public HttpRequestMessage? Request { get; private set; }

        public StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return _respond();
        }
    }

    private static VoxtralSpeechService Voxtral(HttpMessageHandler handler, string apiKey = "key", bool enabled = true)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(VoxtralSpeechService.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://example.test/v1/") });

        return new VoxtralSpeechService(
            factory.Object,
            Options.Create(new SpeechOptions { ApiKey = apiKey, Enabled = enabled }),
            NullLogger<VoxtralSpeechService>.Instance);
    }

    [Fact]
    public async Task Voxtral_Decodes_Audio_And_Sends_The_Documented_Request()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"audio_data\":\"{Convert.ToBase64String(Wav)}\"}}", Encoding.UTF8, "application/json")
        });

        var wav = await Voxtral(handler).SynthesizeAsync("bonjour");

        Assert.Equal(Wav, wav);
        Assert.Equal("https://example.test/v1/audio/speech", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Contains("\"input\":\"bonjour\"", handler.Body);
        Assert.Contains("\"voice_id\":", handler.Body);
        Assert.Contains("\"response_format\":\"wav\"", handler.Body);
    }

    [Fact]
    public async Task Voxtral_Http_Error_Returns_Null_So_The_Fallback_Runs()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("quota")
        });

        Assert.Null(await Voxtral(handler).SynthesizeAsync("bonjour"));
    }

    [Fact]
    public async Task Voxtral_Malformed_Body_Returns_Null()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        });

        Assert.Null(await Voxtral(handler).SynthesizeAsync("bonjour"));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("key", false)]
    public void Voxtral_Not_Ready_Without_Key_Or_When_Disabled(string apiKey, bool enabled)
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.False(Voxtral(handler, apiKey, enabled).IsReady);
    }
}
