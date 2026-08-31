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

    // Les tests continuent de rendre une simple chaine JSON : ce qu ils verifient, c est le
    // comportement de la boucle, pas la carte du HUD. L enveloppe est faite ICI, une fois, plutot
    // que de reecrire les dix appels avec une carte nulle explicite.
    private static async Task<List<AgentEvent>> CollectAsync(
        AgentLoop loop, LLMRequest request, Func<string, string, CancellationToken, Task<string>> executor)
    {
        var events = new List<AgentEvent>();
        Task<ToolOutcome> Enveloppe(string nom, string args, CancellationToken ct)
            => executor(nom, args, ct).ContinueWith(t => new ToolOutcome(t.Result), ct);

        await foreach (var evt in loop.RunAsync(request, Enveloppe)) events.Add(evt);
        return events;
    }

    [Fact]
    public async Task Run_RequestedTool_ActuallyExecuted()
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
    public async Task Run_AfterExecution_ToolsStayAvailable()
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
    public async Task Run_ToolResult_FedBackIntoHistory()
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
    public async Task Run_SeveralTools_ChainAcrossIterations()
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
    public async Task Run_ToolFails_ReportedWithoutBreakingTurn()
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
    public async Task Run_ToolThrows_LoopSurvives()
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
    public async Task Run_InfiniteLoop_CappedByIterationBudget()
    {
        // Un modèle qui redemande sans fin le même outil ne doit pas tourner indéfiniment.
        var turns = Enumerable.Range(0, 10).Select(_ => TurnWithTool("open_app", "{}")).ToArray();
        var client = new ScriptedLLMClient(turns);

        await CollectAsync(BuildLoop(client, maxIterations: 3), BuildRequest(),
            (_, _, _) => Task.FromResult("{\"ok\":true}"));

        // 3 iterations d'outils + 1 tour de conclusion.
        Assert.Equal(4, client.ReceivedRequests.Count);
    }

    [Fact]
    public async Task Run_BudgetSpent_UserStillGetsAnAnswer()
    {
        // Régression vécue : six outils declenches, budget epuise, et l'utilisateur recevait
        // une reponse VIDE. Le dernier tour se fait sans outils pour forcer une conclusion.
        var turns = Enumerable.Range(0, 10).Select(_ => TurnWithTool("list_files", "{}")).ToArray();
        var client = new ScriptedLLMClient(turns);

        var events = await CollectAsync(BuildLoop(client, maxIterations: 2), BuildRequest(),
            (_, _, _) => Task.FromResult("{\"ok\":true}"));

        // Le tour de conclusion ne doit proposer AUCUN outil — sinon la boucle ne se ferme jamais.
        Assert.Null(client.ReceivedRequests[^1].Tools);
        Assert.Contains(events, e => e.Type == AgentEventType.Done);
        Assert.DoesNotContain(events, e => e.Type == AgentEventType.Error);
    }

    [Fact]
    public async Task Run_NoToolNeeded_FinishesInOneIteration()
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
