using Microsoft.Extensions.Logging;
using Moq;
using Orion.Business.LLM;
using Orion.Core.DTOs.Internal.LLM;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;

namespace Orion.Tests.LLM;

/// <summary>
/// La cascade décide quel cerveau parle. Ces tests verrouillent trois choses :
/// l'ORDRE est la politique (distant d'abord), une bascule n'est jamais silencieuse,
/// et l'absence totale de fournisseur échoue franchement au lieu de faire semblant.
/// </summary>
public class LLMCascadeTests
{
    private sealed class FakeClient : ILLMAgentClient
    {
        private readonly bool _alive;

        public FakeClient(LLMProvider provider, string model, bool alive)
        {
            Provider = provider;
            ModelId = model;
            _alive = alive;
        }

        public LLMProvider Provider { get; }
        public string ModelId { get; }
        public int ProbeCalls { get; private set; }
        public int StreamCalls { get; private set; }

        public Task<bool> ProbeAsync(CancellationToken ct = default)
        {
            ProbeCalls++;
            return Task.FromResult(_alive);
        }

        public Task<LLMTurn> StreamTurnAsync(LLMRequest request, Func<string, Task> onToken, CancellationToken ct = default)
        {
            StreamCalls++;
            return Task.FromResult(new LLMTurn { Content = $"reponse de {ModelId}", Model = ModelId });
        }
    }

    private static LLMCascade Build(params ILLMAgentClient[] clients)
        => new(clients, Mock.Of<ILogger<LLMCascade>>());

    private static LLMRequest Request() => new()
    {
        Messages = new List<LLMMessage> { new() { Role = "user", Content = "bonjour" } }
    };

    [Fact]
    public async Task Le_premier_fournisseur_vivant_est_elu()
    {
        var nim = new FakeClient(LLMProvider.Nim, "nemotron", alive: true);
        var local = new FakeClient(LLMProvider.Ollama, "llama3.2:3b", alive: true);

        var cascade = Build(nim, local);
        Assert.True(await cascade.ProbeAsync());

        Assert.Equal(LLMProvider.Nim, cascade.Provider);
        Assert.Equal("nemotron", cascade.ModelId);

        // Le local ne doit même pas être sondé : l'ordre EST la politique.
        Assert.Equal(0, local.ProbeCalls);
    }

    [Fact]
    public async Task Un_fournisseur_mort_fait_basculer_sur_le_suivant()
    {
        var nim = new FakeClient(LLMProvider.Nim, "nemotron", alive: false);
        var local = new FakeClient(LLMProvider.Ollama, "llama3.2:3b", alive: true);

        var cascade = Build(nim, local);
        Assert.True(await cascade.ProbeAsync());

        Assert.Equal(LLMProvider.Ollama, cascade.Provider);
        Assert.Equal(1, nim.ProbeCalls);
        Assert.Equal(1, local.ProbeCalls);
    }

    [Fact]
    public async Task Le_tour_part_vers_le_fournisseur_elu_uniquement()
    {
        var nim = new FakeClient(LLMProvider.Nim, "nemotron", alive: false);
        var local = new FakeClient(LLMProvider.Ollama, "llama3.2:3b", alive: true);

        var cascade = Build(nim, local);
        await cascade.ProbeAsync();

        var turn = await cascade.StreamTurnAsync(Request(), _ => Task.CompletedTask);

        Assert.Equal("reponse de llama3.2:3b", turn.Content);
        Assert.Equal(0, nim.StreamCalls);
        Assert.Equal(1, local.StreamCalls);
    }

    [Fact]
    public async Task Aucun_fournisseur_vivant_la_sonde_echoue_franchement()
    {
        var cascade = Build(
            new FakeClient(LLMProvider.Nim, "nemotron", alive: false),
            new FakeClient(LLMProvider.Ollama, "llama3.2:3b", alive: false));

        Assert.False(await cascade.ProbeAsync());
        Assert.Equal(LLMProvider.None, cascade.Provider);
        Assert.Equal("aucun", cascade.ModelId);
    }

    [Fact]
    public async Task Sans_fournisseur_elu_un_tour_leve_au_lieu_de_faire_semblant()
    {
        // Régression du silence : un LLM absent doit CASSER visiblement, pas répondre du vide.
        var cascade = Build(new FakeClient(LLMProvider.Nim, "nemotron", alive: false));
        await cascade.ProbeAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => cascade.StreamTurnAsync(Request(), _ => Task.CompletedTask));

        Assert.Contains("Aucun fournisseur LLM operationnel", ex.Message);
    }

    [Fact]
    public void Une_cascade_vide_est_refusee_a_la_construction()
    {
        Assert.Throws<InvalidOperationException>(() => Build());
    }
}
