using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Orion.Business.Agents;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Internal.LLM;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;

namespace Orion.Tests.Agents;

/// <summary>
/// Garde-fous de régression de la boucle agent.
/// Chacun de ces tests échoue si l'une des causes racines documentées dans
/// docs/jarvis-gap-analysis.md revient.
/// </summary>
public class AgentLoopTests
{
    /// <summary>Client LLM scripté : rejoue une liste de tours et enregistre ce qu'il a reçu.</summary>
    private sealed class ScriptedLLMClient : ILLMAgentClient
    {
        private readonly Queue<LLMTurn> _turns;

        public ScriptedLLMClient(params LLMTurn[] turns) => _turns = new Queue<LLMTurn>(turns);

        public List<LLMRequest> ReceivedRequests { get; } = new();

        public LLMProvider Provider => LLMProvider.Ollama;
        public string ModelId => "scripted";
        public Task<bool> ProbeAsync(CancellationToken ct = default) => Task.FromResult(true);

        public async Task<LLMTurn> StreamTurnAsync(
            LLMRequest request, Func<string, Task> onToken, CancellationToken ct = default)
        {
            // Copie profonde des messages : la boucle réutilise la même liste d'un tour à l'autre.
            ReceivedRequests.Add(new LLMRequest
            {
                SystemPrompt = request.SystemPrompt,
                Messages = request.Messages.Select(m => new LLMMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    ToolCalls = m.ToolCalls,
                    ToolCallId = m.ToolCallId,
                    ToolName = m.ToolName
                }).ToList(),
                Tools = request.Tools
            });

            var turn = _turns.Count > 0
                ? _turns.Dequeue()
                : new LLMTurn { Content = "fin" };

            if (!string.IsNullOrEmpty(turn.Content)) await onToken(turn.Content);
            return turn;
        }
    }

    private static AgentLoop BuildLoop(ILLMAgentClient client, int maxIterations = 6) =>
        new(client,
            Options.Create(new AgentOptions { MaxToolIterations = maxIterations }),
            Mock.Of<ILogger<AgentLoop>>());

    private static LLMRequest BuildRequest() => new()
    {
        SystemPrompt = "Tu es ORION.",
        Messages = new List<LLMMessage> { new() { Role = "user", Content = "Ouvre Notepad." } },
        Tools = new List<ToolDefinition> { new() { Name = "open_app", Description = "Ouvre une app" } }
    };

    private static LLMTurn TurnWithTool(string tool, string argsJson) => new()
    {
        Content = string.Empty,
        ToolCalls = new List<LLMToolCall>
        {
            new() { Id = "call_1", Name = tool, ArgumentsJson = argsJson }
        }
    };

    private static async Task<List<AgentEvent>> CollectAsync(
        AgentLoop loop, LLMRequest request, Func<string, string, CancellationToken, Task<string>> executor)
    {
        var events = new List<AgentEvent>();
        await foreach (var evt in loop.RunAsync(request, executor)) events.Add(evt);
        return events;
    }

    [Fact]
    public async Task Un_outil_demande_est_reellement_execute()
    {
        var client = new ScriptedLLMClient(
            TurnWithTool("open_app", "{\"appName\":\"notepad\"}"),
            new LLMTurn { Content = "Notepad est ouvert." });

        var executed = new List<(string Tool, string Args)>();

        var events = await CollectAsync(BuildLoop(client), BuildRequest(), (tool, args, _) =>
        {
            executed.Add((tool, args));
            return Task.FromResult("{\"ok\":true}");
        });

        Assert.Single(executed);
        Assert.Equal("open_app", executed[0].Tool);
        Assert.Contains("notepad", executed[0].Args);

        Assert.Contains(events, e => e.Type == AgentEventType.ToolStart && e.ToolName == "open_app");
        Assert.Contains(events, e => e.Type == AgentEventType.ToolResult && e.ToolOk == true);
        Assert.Contains(events, e => e.Type == AgentEventType.Done);
    }

    [Fact]
    public async Task Les_outils_restent_disponibles_apres_une_execution()
    {
        // Régression §1.4 : l'ancien client relançait le modèle SANS les outils après la
        // première exécution — le chaînage multi-outils était donc impossible.
        var client = new ScriptedLLMClient(
            TurnWithTool("web_search", "{\"query\":\"x\"}"),
            new LLMTurn { Content = "Voila." });

        await CollectAsync(BuildLoop(client), BuildRequest(),
            (_, _, _) => Task.FromResult("{\"results\":[]}"));

        Assert.Equal(2, client.ReceivedRequests.Count);
        Assert.NotNull(client.ReceivedRequests[1].Tools);
        Assert.NotEmpty(client.ReceivedRequests[1].Tools!);
    }

    [Fact]
    public async Task Le_resultat_de_l_outil_est_reinjecte_dans_l_historique()
    {
        var client = new ScriptedLLMClient(
            TurnWithTool("web_search", "{\"query\":\"coupe du monde\"}"),
            new LLMTurn { Content = "L'Argentine." });

        await CollectAsync(BuildLoop(client), BuildRequest(),
            (_, _, _) => Task.FromResult("{\"winner\":\"Argentine\"}"));

        var secondTurn = client.ReceivedRequests[1].Messages;

        var assistant = Assert.Single(secondTurn, m => m.Role == "assistant" && m.ToolCalls is { Count: > 0 });
        Assert.Equal("web_search", assistant.ToolCalls![0].Name);

        var toolMessage = Assert.Single(secondTurn, m => m.Role == "tool");
        Assert.Contains("Argentine", toolMessage.Content);
        Assert.Equal("web_search", toolMessage.ToolName);
    }

    [Fact]
    public async Task Plusieurs_outils_s_enchainent_sur_plusieurs_iterations()
    {
        var client = new ScriptedLLMClient(
            TurnWithTool("web_search", "{\"query\":\"x\"}"),
            TurnWithTool("write_file", "{\"path\":\"a.txt\"}"),
            new LLMTurn { Content = "Fait." });

        var executed = new List<string>();

        var events = await CollectAsync(BuildLoop(client), BuildRequest(), (tool, _, _) =>
        {
            executed.Add(tool);
            return Task.FromResult("{\"ok\":true}");
        });

        Assert.Equal(new[] { "web_search", "write_file" }, executed);
        Assert.Equal(3, client.ReceivedRequests.Count);
        Assert.Contains(events, e => e.Type == AgentEventType.Done);
    }

    [Fact]
    public async Task Un_outil_en_echec_est_signale_sans_casser_le_tour()
    {
        var client = new ScriptedLLMClient(
            TurnWithTool("open_app", "{}"),
            new LLMTurn { Content = "Le daemon est hors ligne." });

        var events = await CollectAsync(BuildLoop(client), BuildRequest(),
            (_, _, _) => Task.FromResult("{\"error\":\"Daemon non connecte\"}"));

        var result = Assert.Single(events, e => e.Type == AgentEventType.ToolResult);
        Assert.False(result.ToolOk);
        Assert.Contains(events, e => e.Type == AgentEventType.Done);
    }

    [Fact]
    public async Task Une_exception_d_outil_ne_fait_pas_tomber_la_boucle()
    {
        var client = new ScriptedLLMClient(
            TurnWithTool("open_app", "{}"),
            new LLMTurn { Content = "Reessaie plus tard." });

        var events = await CollectAsync(BuildLoop(client), BuildRequest(),
            (_, _, _) => throw new InvalidOperationException("boom"));

        var result = Assert.Single(events, e => e.Type == AgentEventType.ToolResult);
        Assert.False(result.ToolOk);
        Assert.Contains(events, e => e.Type == AgentEventType.Done);
    }

    [Fact]
    public async Task Le_budget_d_iterations_borne_une_boucle_infinie()
    {
        // Un modèle qui redemande sans fin le même outil ne doit pas tourner indéfiniment,
        // et l'arrêt doit être VISIBLE — pas un silence.
        var turns = Enumerable.Range(0, 10).Select(_ => TurnWithTool("open_app", "{}")).ToArray();
        var client = new ScriptedLLMClient(turns);

        var events = await CollectAsync(BuildLoop(client, maxIterations: 3), BuildRequest(),
            (_, _, _) => Task.FromResult("{\"ok\":true}"));

        Assert.Equal(3, client.ReceivedRequests.Count);
        var error = Assert.Single(events, e => e.Type == AgentEventType.Error);
        Assert.Contains("Budget", error.Text);
    }

    [Fact]
    public async Task Une_reponse_sans_outil_termine_en_une_iteration()
    {
        var client = new ScriptedLLMClient(new LLMTurn { Content = "Bonjour." });

        var events = await CollectAsync(BuildLoop(client), BuildRequest(),
            (_, _, _) => Task.FromResult("{}"));

        Assert.Single(client.ReceivedRequests);
        Assert.DoesNotContain(events, e => e.Type == AgentEventType.ToolStart);
        Assert.Contains(events, e => e.Type == AgentEventType.Token && e.Text == "Bonjour.");
        Assert.Contains(events, e => e.Type == AgentEventType.Done);
    }
}
