using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orion.Api.WebSockets;
using Orion.Core.Interfaces.Services;

namespace Orion.Tests.Services;

/// <summary>Un seul micro a la fois, et la fin d une vieille connexion ne chasse pas la nouvelle.</summary>
public class VoiceConnectionRegistryTests
{
    private static VoiceWebSocketHandler Handler() => new(
        NullLogger<VoiceWebSocketHandler>.Instance,
        Mock.Of<IServiceScopeFactory>(),
        Mock.Of<IWhisperService>());

    [Fact]
    public void First_Connection_Replaces_Nothing()
    {
        Assert.Null(new VoiceConnectionRegistry().Replace(Handler()));
    }

    [Fact]
    public void New_Connection_Returns_The_One_It_Replaces()
    {
        var registry = new VoiceConnectionRegistry();
        var first = Handler();
        registry.Replace(first);

        Assert.Same(first, registry.Replace(Handler()));
    }

    [Fact]
    public void Releasing_A_Replaced_Connection_Keeps_The_Current_One()
    {
        var registry = new VoiceConnectionRegistry();
        var first = Handler();
        var second = Handler();
        registry.Replace(first);
        registry.Replace(second);

        registry.Release(first);

        Assert.Same(second, registry.Replace(Handler()));
    }

    [Fact]
    public void Releasing_The_Current_Connection_Frees_The_Slot()
    {
        var registry = new VoiceConnectionRegistry();
        var only = Handler();
        registry.Replace(only);

        registry.Release(only);

        Assert.Null(registry.Replace(Handler()));
    }

    [Fact]
    public async Task Superseding_A_Handler_Without_Socket_Does_Not_Throw()
    {
        await Handler().SupersedeAsync();
    }
}
