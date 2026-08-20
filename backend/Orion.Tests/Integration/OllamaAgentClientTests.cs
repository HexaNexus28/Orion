using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Orion.Business.LLM;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Requests;
using Orion.Core.Interfaces.LLM;

namespace Orion.Tests.Integration;

/// <summary>
/// Tests d'intégration contre l'Ollama LOCAL — nécessitent `ollama serve` et le modèle de repli.
/// Exclure avec : dotnet test --filter "Category!=Integration"
///
/// C'est LE test qui aurait attrapé la cause racine : l'ancien client n'envoyait pas le champ
/// `tools` dans le payload de streaming, donc le modèle n'apprenait jamais qu'il avait des outils
/// (docs/jarvis-gap-analysis.md §1.3). Un test unitaire mocké ne peut pas voir ça — seule une
/// vraie requête au vrai serveur le prouve.
/// </summary>
[Trait("Category", "Integration")]
public class OllamaAgentClientTests
{
    private const string LocalModel = "llama3.2:3b";

    private static ILLMAgentClient BuildClient()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(OllamaAgentClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("http://localhost:11434");
            client.Timeout = TimeSpan.FromMinutes(3);
        });

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var options = Options.Create(new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            Model = LocalModel,
            FallbackModel = LocalModel,
            NumCtx = 4096
        });

        return new OllamaAgentClient(factory, options, Mock.Of<ILogger<OllamaAgentClient>>());
    }

    private static LLMRequest RequestWithTool(string message) => new()
    {
        SystemPrompt = "Tu es ORION. Utilise les outils disponibles quand la demande le justifie.",
        Messages = new List<LLMMessage> { new() { Role = "user", Content = message } },
        Temperature = 0f,
        Tools = new List<ToolDefinition>
        {
            new()
            {
                Name = "open_app",
                Description = "Ouvre une application sur le PC Windows",
                Parameters = System.Text.Json.Nodes.JsonNode
                    .Parse("""
                    {
                      "type": "object",
                      "properties": { "appName": { "type": "string" } },
                      "required": ["appName"]
                    }
                    """)!.AsObject()
            }
        }
    };

    [Fact]
    public async Task Le_modele_repond_reellement_a_la_sonde()
    {
        // Vérifier une ressource distante = l'APPELER. `ollama list` peut afficher un modèle
        // retiré ou verrouillé par abonnement (§1.10).
        var client = BuildClient();

        var alive = await client.ProbeAsync(CancellationToken.None);

        Assert.True(alive, $"Ollama doit tourner en local avec le modele {LocalModel}");
    }

    [Fact]
    public async Task Les_tools_sont_bien_transmis_EN_STREAMING()
    {
        // LA régression à empêcher : si le champ `tools` disparaît du payload de streaming,
        // le modèle ne demandera jamais d'outil et ce test rougit.
        var client = BuildClient();
        var tokens = new List<string>();

        var turn = await client.StreamTurnAsync(
            RequestWithTool("Ouvre Notepad sur mon PC."),
            token => { tokens.Add(token); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.True(turn.HasToolCalls,
            "Le modele n'a demande aucun outil — le champ 'tools' n'a probablement pas ete envoye.");
        Assert.Equal("open_app", turn.ToolCalls[0].Name);
        Assert.Contains("otepad", turn.ToolCalls[0].ArgumentsJson);
    }

    [Fact]
    public async Task Les_arguments_d_outil_sont_du_JSON_exploitable()
    {
        // L'endpoint OpenAI-compatible d'Ollama renvoie ici du JSON malformé en streaming ;
        // l'endpoint natif renvoie un objet propre. Ce test verrouille ce choix de transport.
        var client = BuildClient();

        var turn = await client.StreamTurnAsync(
            RequestWithTool("Ouvre Notepad sur mon PC."),
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.True(turn.HasToolCalls);

        var parsed = System.Text.Json.Nodes.JsonNode.Parse(turn.ToolCalls[0].ArgumentsJson);
        Assert.NotNull(parsed);
        Assert.NotNull(parsed!.AsObject()["appName"]);
    }

    [Fact]
    public async Task Une_requete_sans_outil_streame_du_texte_token_par_token()
    {
        // Sans outils declares, le modele ne peut que produire du texte : on verifie que les
        // tokens arrivent bien au fil de l'eau (ResponseHeadersRead) et non d'un seul bloc.
        var client = BuildClient();
        var tokens = new List<string>();

        var request = new LLMRequest
        {
            SystemPrompt = "Reponds en une phrase courte.",
            Messages = new List<LLMMessage> { new() { Role = "user", Content = "Dis bonjour." } },
            Temperature = 0f
        };

        var turn = await client.StreamTurnAsync(
            request,
            token => { tokens.Add(token); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.False(turn.HasToolCalls);
        Assert.True(tokens.Count > 1, "Le texte doit arriver en plusieurs chunks, pas d'un seul bloc.");
        Assert.False(string.IsNullOrWhiteSpace(turn.Content));
    }
}
