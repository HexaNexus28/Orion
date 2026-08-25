using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Internal.LLM;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.LLM;

namespace Orion.Business.Agents;

/// <summary>
/// Boucle agent — le point d'application unique du raisonnement d'ORION.
///
/// Tant que le modèle demande des outils, on les exécute, on réinjecte les résultats dans
/// l'historique et on le rappelle. C'est ce qui rend possible le chaînage
/// (« cherche X, puis écris le résultat, puis commit ») — impossible avant, car l'ancien
/// client relançait le modèle SANS les outils après la première exécution.
/// </summary>
public class AgentLoop : IAgentLoop
{
    private readonly ILLMAgentClient _client;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentLoop> _logger;

    public AgentLoop(ILLMAgentClient client, IOptions<AgentOptions> options, ILogger<AgentLoop> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        LLMRequest request,
        Func<string, string, CancellationToken, Task<string>> toolExecutor,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Copie de travail : la boucle enrichit l'historique à chaque itération sans muter
        // la requête d'origine.
        var messages = new List<LLMMessage>(request.Messages);
        var maxIterations = Math.Max(1, _options.MaxToolIterations);

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var turnRequest = new LLMRequest
            {
                SystemPrompt = request.SystemPrompt,
                Messages = messages,
                Model = request.Model,
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens,
                Tools = request.Tools
            };

            // Les tokens arrivent par callback ; on les republie en événements via un canal
            // pour pouvoir les yield au fil de l'eau.
            var tokens = Channel.CreateUnbounded<string>();
            var turnTask = RunTurnAsync(turnRequest, tokens, ct);

            await foreach (var token in tokens.Reader.ReadAllAsync(ct))
            {
                yield return AgentEvent.Token(token, iteration);
            }

            LLMTurn? turn = null;
            string? failure = null;
            try
            {
                turn = await turnTask;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AgentLoop] Iteration {Iteration} echouee", iteration);
                failure = ex.Message;
            }

            if (failure is not null)
            {
                yield return AgentEvent.Error(failure, iteration);
                yield break;
            }

            if (!turn!.HasToolCalls)
            {
                _logger.LogInformation(
                    "[AgentLoop] Termine en {Iterations} iteration(s) — modele {Model}",
                    iteration, turn.Model);
                yield return AgentEvent.Done(iteration);
                yield break;
            }

            // Le message assistant qui porte les tool_calls doit être conservé dans
            // l'historique, sinon le modèle ne relie pas les résultats à ses demandes.
            messages.Add(new LLMMessage
            {
                Role = "assistant",
                Content = turn.Content,
                ToolCalls = turn.ToolCalls
            });

            foreach (var call in turn.ToolCalls)
            {
                yield return AgentEvent.ToolStart(call.Name, call.ArgumentsJson, iteration);

                var (resultJson, ok) = await ExecuteToolAsync(toolExecutor, call, ct);

                _logger.LogInformation(
                    "[AgentLoop] Outil {Tool} — succes: {Ok} (iteration {Iteration})",
                    call.Name, ok, iteration);

                yield return AgentEvent.ToolResult(call.Name, ok, Summarize(resultJson), iteration);

                messages.Add(new LLMMessage
                {
                    Role = "tool",
                    Content = resultJson,
                    ToolCallId = call.Id,
                    ToolName = call.Name
                });
            }
        }

        // Budget épuisé : le modèle redemande des outils sans converger.
        // On ne rend PAS la main sur une réponse vide — c'est ce que voyait l'utilisateur :
        // six outils déclenchés et rien à lire. Un dernier tour SANS outils force le modèle
        // à conclure avec ce qu'il a déjà récolté.
        _logger.LogWarning("[AgentLoop] Budget epuise ({Max}) — tour de conclusion sans outils", maxIterations);

        var closing = new LLMRequest
        {
            SystemPrompt = request.SystemPrompt,
            Messages = messages,
            Model = request.Model,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            Tools = null // <- la contrainte : impossible d'appeler un outil de plus
        };

        var lastTokens = Channel.CreateUnbounded<string>();
        var lastTask = RunTurnAsync(closing, lastTokens, ct);

        await foreach (var token in lastTokens.Reader.ReadAllAsync(ct))
        {
            yield return AgentEvent.Token(token, maxIterations);
        }

        string? closingFailure = null;
        try
        {
            await lastTask;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentLoop] Tour de conclusion echoue");
            closingFailure = ex.Message;
        }

        if (closingFailure is not null)
        {
            yield return AgentEvent.Error(closingFailure, maxIterations);
            yield break;
        }

        yield return AgentEvent.Done(maxIterations);
    }

    private async Task<LLMTurn> RunTurnAsync(LLMRequest request, Channel<string> tokens, CancellationToken ct)
    {
        try
        {
            return await _client.StreamTurnAsync(
                request,
                async token => await tokens.Writer.WriteAsync(token, ct),
                ct);
        }
        finally
        {
            tokens.Writer.Complete();
        }
    }

    private async Task<(string Json, bool Ok)> ExecuteToolAsync(
        Func<string, string, CancellationToken, Task<string>> toolExecutor,
        LLMToolCall call,
        CancellationToken ct)
    {
        try
        {
            var json = await toolExecutor(call.Name, call.ArgumentsJson, ct);
            return (json, !CarriesError(json));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentLoop] Outil {Tool} a leve une exception", call.Name);
            return (JsonSerializer.Serialize(new { error = ex.Message }), false);
        }
    }

    private static bool CarriesError(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonObject obj && obj.ContainsKey("error");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string Summarize(string json)
    {
        var max = Math.Max(80, _options.ToolSummaryMaxChars);
        return json.Length <= max ? json : json[..max] + "...";
    }
}
