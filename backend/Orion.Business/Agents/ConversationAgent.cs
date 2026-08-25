using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Business.LLM;
using Orion.Core.DTOs.Internal.Tools;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Agents;

/// <summary>
/// Orchestre un tour de conversation : persistance, contexte RAG, prompt, outils —
/// puis délègue TOUT le raisonnement à <see cref="IAgentLoop"/>.
///
/// L'agent ne parle plus jamais au LLM directement : c'est ce qui garantit que le chemin
/// streamé et le chemin agrégé se comportent à l'identique.
/// </summary>
public class ConversationAgent : IConversationAgent
{
    private readonly IAgentLoop _agentLoop;
    private readonly ILLMAgentClient _llmClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmbeddingService _embeddingService;
    private readonly PromptBuilder _promptBuilder;
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolInvoker _toolInvoker;
    private readonly IDaemonClient _daemonClient;
    private readonly ILogger<ConversationAgent> _logger;

    public ConversationAgent(
        IAgentLoop agentLoop,
        ILLMAgentClient llmClient,
        IUnitOfWork unitOfWork,
        IEmbeddingService embeddingService,
        PromptBuilder promptBuilder,
        IToolRegistry toolRegistry,
        IToolInvoker toolInvoker,
        IDaemonClient daemonClient,
        ILogger<ConversationAgent> logger)
    {
        _agentLoop = agentLoop;
        _llmClient = llmClient;
        _unitOfWork = unitOfWork;
        _embeddingService = embeddingService;
        _promptBuilder = promptBuilder;
        _toolRegistry = toolRegistry;
        _toolInvoker = toolInvoker;
        _daemonClient = daemonClient;
        _logger = logger;
    }

    // ── Tour complet (réponse agrégée) ──────────────────────────────────────────

    public async Task<ApiResponse<ChatResponse>> ProcessAsync(ChatRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var prepared = await PrepareStreamAsync(request, ct);
        if (!prepared.Success || prepared.Data is null)
            return ApiResponse<ChatResponse>.ErrorResponse(prepared.Message ?? "Preparation impossible", prepared.StatusCode);

        var context = prepared.Data;
        var content = new StringBuilder();
        var toolsCalled = new List<ToolCallDto>();
        ToolCallDto? pending = null;
        string? failure = null;

        await foreach (var evt in StreamLLMAsync(context, ct))
        {
            switch (evt.Type)
            {
                case AgentEventType.Token:
                    content.Append(evt.Text);
                    break;

                case AgentEventType.ToolStart:
                    pending = new ToolCallDto
                    {
                        ToolName = evt.ToolName ?? "?",
                        Input = evt.ToolArgs ?? "{}"
                    };
                    toolsCalled.Add(pending);
                    break;

                case AgentEventType.ToolResult:
                    if (pending is not null) pending.Result = evt.ToolSummary;
                    pending = null;
                    break;

                case AgentEventType.Error:
                    failure = evt.Text;
                    break;
            }
        }

        stopwatch.Stop();

        if (failure is not null && content.Length == 0)
            return ApiResponse<ChatResponse>.ErrorResponse(failure, 503);

        _logger.LogInformation(
            "[ConversationAgent] Tour traite en {ElapsedMs}ms — session {SessionId}, {Tools} outil(s)",
            stopwatch.ElapsedMilliseconds, context.SessionId, toolsCalled.Count);

        return ApiResponse<ChatResponse>.SuccessResponse(new ChatResponse
        {
            Response = content.ToString(),
            SessionId = context.SessionId,
            LlmProvider = _llmClient.Provider,
            MemoryUsed = context.MemoryUsed,
            ToolsCalled = toolsCalled.Count > 0 ? toolsCalled : null
        });
    }

    // ── Phase 1 : préparation (DB + prompt) ─────────────────────────────────────

    public async Task<ApiResponse<StreamContext>> PrepareStreamAsync(ChatRequest request, CancellationToken ct = default)
    {
        try
        {
            Conversation conversation;
            try
            {
                conversation = await ResolveConversationAsync(request, ct);

                await _unitOfWork.Messages.AddAsync(new Message
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    Role = MessageRole.User,
                    Content = request.Message,
                    CreatedAt = DateTime.UtcNow
                }, ct);

                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Un tour annulé (barge-in) n'est PAS une panne de base. Sans ce filtre, une
                // interruption normale se signalait « Base de donnees inaccessible » — un
                // diagnostic faux qui envoie chercher le problème au mauvais endroit.
                throw;
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "[ConversationAgent/Prepare] Base inaccessible — {Type}: {Msg}",
                    dbEx.GetType().Name, dbEx.Message);
                return ApiResponse<StreamContext>.ErrorResponse($"Base de donnees inaccessible: {dbEx.Message}", 503);
            }

            var history = await _unitOfWork.Messages.GetByConversationIdAsync(conversation.Id, ct);
            var messages = history.TakeLast(9).Select(m => new LLMMessage
            {
                Role = m.Role.ToString().ToLowerInvariant(),
                Content = m.Content
            }).ToList();
            messages.Add(new LLMMessage { Role = "user", Content = request.Message });

            // Le catalogue se trie par UTILITÉ, pas par disponibilité.
            //
            // Demander dans le prompt « n'appelle pas les outils [PC requis] » ne suffit pas :
            // mesuré le 2026-08-20, `llama3.2:3b` appelle quand même `type_text` pour « dis
            // bonjour ». Retirer l'outil rend l'erreur impossible par construction au lieu de
            // compter sur l'obéissance du modèle.
            //
            // Mais depuis la file différée, « indisponible » ne veut plus dire « inutile » :
            // « commit le travail » attend très bien le réveil du PC, alors que « qu'y a-t-il
            // dans ce dossier ? » ne vaut plus rien demain. Ce sont donc les outils DIFFÉRABLES
            // qui restent au catalogue, et les lectures qui en sortent.
            var daemonConnected = _daemonClient.IsConnected;
            var registered = _toolRegistry.GetAllTools()
                .Where(t => daemonConnected || !t.RequiresDaemon || t.IsDeferrable)
                .ToList();

            var tools = registered
                .Select(t => new ToolDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.InputSchema
                })
                .ToList();

            var profile = await BuildUserProfileAsync(ct);
            var memories = await BuildRelevantMemoriesAsync(request.Message, ct);

            // Les outils réellement enregistrés, avec leurs descriptions et métadonnées.
            // La liste passée ici était systématiquement vide : la section « outils » du prompt
            // ne s'affichait donc jamais (docs/jarvis-gap-analysis.md §1.7).
            var systemPrompt = _promptBuilder.BuildSystemPrompt(
                profile,
                memories,
                registered,
                daemonConnected,
                _llmClient.Provider,
                voiceMode: request.VoiceMode);

            _logger.LogInformation(
                "[ConversationAgent/Prepare] Session {Sid} — {Tools} outils proposes (PC joignable: {Daemon}), {Memories} souvenir(s)",
                conversation.Id, tools.Count, daemonConnected, memories.Count);

            return ApiResponse<StreamContext>.SuccessResponse(new StreamContext
            {
                SessionId = conversation.Id,
                ConversationId = conversation.Id,
                MemoryUsed = memories.Count > 0,
                UserMessage = request.Message,
                LlmRequest = new LLMRequest
                {
                    SystemPrompt = systemPrompt,
                    Messages = messages,
                    Temperature = 0.7f,
                    Tools = tools.Count > 0 ? tools : null
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConversationAgent/Prepare] Echec");
            return ApiResponse<StreamContext>.ErrorResponse($"Erreur preparation: {ex.Message}");
        }
    }

    // ── Phase 2 : boucle agent ──────────────────────────────────────────────────

    public async IAsyncEnumerable<AgentEvent> StreamLLMAsync(
        StreamContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var content = new StringBuilder();

        // Le contexte suit l'appel jusqu'à la file : si l'outil doit être différé, ORION doit
        // encore savoir à quel fil répondre et quelle phrase rappeler au réveil du PC.
        var invocation = new ToolInvocationContext(
            context.ConversationId,
            ToolInvocationContext.OrigineChat,
            context.UserMessage);

        await foreach (var evt in _agentLoop.RunAsync(
            context.LlmRequest,
            (nom, arguments, token) => ExecuteToolAsync(nom, arguments, invocation, token),
            ct))
        {
            if (evt.Type == AgentEventType.Token) content.Append(evt.Text);
            yield return evt;
        }

        // Le modèle peut rendre zéro caractère ET zéro appel d'outil, sans erreur : observé le
        // 2026-08-21, PC éteint, sur une demande qu'aucun outil disponible ne pouvait servir.
        // ORION renvoyait alors une chaîne vide — c'est-à-dire du SILENCE, la panne la plus
        // coûteuse de ce projet, celle qui ne se signale jamais.
        //
        // On ne fabrique pas de contenu à sa place : on dit qu'il n'y a pas eu de réponse, et
        // on le LOGGUE en avertissement. Un repli qui masque au lieu d'alerter serait un bug
        // de plus, pas un correctif.
        if (content.Length == 0)
        {
            _logger.LogWarning(
                "[ConversationAgent/Stream] Le modèle n'a rien produit — session {SessionId}, PC joignable: {Daemon}",
                context.ConversationId, _daemonClient.IsConnected);

            const string aveu = "Je n'ai rien à te répondre sur ce coup-là — le modèle n'a rien produit. "
                              + "Reformule, ou redemande-le-moi.";
            content.Append(aveu);
            yield return AgentEvent.Token(aveu, 1);
        }

        // Sauvegarde au mieux — une panne d'écriture ne doit pas casser le flux déjà rendu.
        try
        {
            await _unitOfWork.Messages.AddAsync(new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = context.ConversationId,
                Role = MessageRole.Assistant,
                Content = content.ToString(),
                CreatedAt = DateTime.UtcNow
            }, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation("[ConversationAgent/Stream] Termine — {Chars} caracteres", content.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConversationAgent/Stream] Sauvegarde de la reponse impossible");
        }
    }

    public async IAsyncEnumerable<AgentEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var prepared = await PrepareStreamAsync(request, ct);
        if (!prepared.Success || prepared.Data is null)
        {
            _logger.LogError("[ConversationAgent/Stream] Preparation echouee: {Msg}", prepared.Message);
            yield return AgentEvent.Error(prepared.Message ?? "Preparation impossible", 0);
            yield break;
        }

        await foreach (var evt in StreamLLMAsync(prepared.Data, ct))
        {
            yield return evt;
        }
    }

    // ── Privé ───────────────────────────────────────────────────────────────────

    private async Task<Conversation> ResolveConversationAsync(ChatRequest request, CancellationToken ct)
    {
        if (request.SessionId.HasValue)
        {
            var existing = await _unitOfWork.Conversations.GetByIdAsync(request.SessionId.Value, ct);
            if (existing is not null) return existing;

            var restored = new Conversation
            {
                Id = request.SessionId.Value,
                Type = ConversationType.Chat,
                StartedAt = DateTime.UtcNow,
                LlmProvider = _llmClient.Provider
            };
            await _unitOfWork.Conversations.AddAsync(restored, ct);
            return restored;
        }

        var created = new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Chat,
            StartedAt = DateTime.UtcNow,
            LlmProvider = _llmClient.Provider
        };
        await _unitOfWork.Conversations.AddAsync(created, ct);
        return created;
    }

    /// <summary>
    /// Rend le résultat d'un outil à la boucle agent, sous forme de JSON que le modèle relit.
    ///
    /// La DÉCISION (exécuter, différer, refuser) appartient à <see cref="IToolInvoker"/> :
    /// l'agent ne fait plus que traduire. C'est ce qui garantit que l'API outils et la
    /// conversation obéissent aux mêmes règles.
    /// </summary>
    private async Task<string> ExecuteToolAsync(
        string toolName,
        string argumentsJson,
        ToolInvocationContext invocation,
        CancellationToken ct)
    {
        JsonObject input;
        try
        {
            input = string.IsNullOrWhiteSpace(argumentsJson)
                ? new JsonObject()
                : JsonNode.Parse(argumentsJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            input = new JsonObject();
        }

        var result = await _toolInvoker.InvokeAsync(toolName, input, invocation, ct);

        if (result.Success && result.Data is { Success: true })
            return JsonSerializer.Serialize(result.Data.Data);

        var error = result.Data?.Error ?? result.Message ?? "Execution de l'outil echouee";
        return JsonSerializer.Serialize(new { error });
    }

    private async Task<Dictionary<string, string>> BuildUserProfileAsync(CancellationToken ct)
    {
        try
        {
            var profiles = await _unitOfWork.UserProfile.GetAllAsync(ct);
            var dict = profiles.ToDictionary(p => p.Key, p => p.Value);
            if (dict.Count > 0) return dict;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ConversationAgent] Profil utilisateur illisible, repli par defaut");
        }

        return new Dictionary<string, string> { ["name"] = "Utilisateur" };
    }

    private async Task<List<MemoryVector>> BuildRelevantMemoriesAsync(string message, CancellationToken ct)
    {
        try
        {
            // Ne pas payer un embedding pour fouiller une memoire vide.
            // Mesure du 2026-08-20 : ~800 ms a chaud, sur le CHEMIN CRITIQUE de chaque tour,
            // alors que `memory_vectors` ne contenait aucune ligne. Pure perte.
            if (await _unitOfWork.Memory.CountAsync(null, ct) == 0)
            {
                _logger.LogDebug("[ConversationAgent] Memoire vide — recherche RAG ignoree");
                return new List<MemoryVector>();
            }

            var embedding = await _embeddingService.GenerateEmbeddingAsync(message, EmbeddingInputType.Query, ct);
            if (embedding.Success && embedding.Data?.Length > 0)
            {
                var memories = await _unitOfWork.Memory.SearchSimilarAsync(embedding.Data, 5, ct);
                return memories.ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ConversationAgent] RAG indisponible, on continue sans souvenirs");
        }

        return new List<MemoryVector>();
    }
}
