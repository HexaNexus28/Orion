using System.Text.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
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
using Orion.Business.LLM;

namespace Orion.Business.Agents;

public class ConversationAgent : IConversationAgent
{
    private readonly ILLMRouter _llmRouter;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmbeddingService _embeddingService;
    private readonly PromptBuilder _promptBuilder;
    private readonly IToolRegistry _toolRegistry;
    private readonly IDaemonClient _daemonClient;
    private readonly ILogger<ConversationAgent> _logger;

    public ConversationAgent(
        ILLMRouter llmRouter,
        IUnitOfWork unitOfWork,
        IEmbeddingService embeddingService,
        PromptBuilder promptBuilder,
        IToolRegistry toolRegistry,
        IDaemonClient daemonClient,
        ILogger<ConversationAgent> logger)
    {
        _llmRouter = llmRouter;
        _unitOfWork = unitOfWork;
        _embeddingService = embeddingService;
        _promptBuilder = promptBuilder;
        _toolRegistry = toolRegistry;
        _daemonClient = daemonClient;
        _logger = logger;
    }

    public async Task<ApiResponse<ChatResponse>> ProcessAsync(ChatRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 1. Get or create conversation
            Conversation conversation;
            if (request.SessionId.HasValue)
            {
                conversation = await _unitOfWork.Conversations.GetByIdAsync(request.SessionId.Value, ct);
                if (conversation == null)
                {
                    return ApiResponse<ChatResponse>.NotFoundResponse("Session introuvable");
                }
            }
            else
            {
                conversation = new Conversation
                {
                    Id = Guid.NewGuid(),
                    Type = ConversationType.Chat,
                    StartedAt = DateTime.UtcNow,
                    LlmProvider = _llmRouter.ActiveProvider
                };
                await _unitOfWork.Conversations.AddAsync(conversation, ct);
            }

            // 2. Save user message
            var userMessage = new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Role = MessageRole.User,
                Content = request.Message,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Messages.AddAsync(userMessage, ct);

            // 3. Build message history (last 10) - include current message since SaveChanges hasn't happened yet
            var recentMessages = await _unitOfWork.Messages.GetByConversationIdAsync(conversation.Id, ct);
            var messageHistory = recentMessages.TakeLast(9).Select(m => new LLMMessage
            {
                Role = m.Role.ToString().ToLower(),
                Content = m.Content
            }).ToList();

            // Add current user message (not yet saved to DB)
            messageHistory.Add(new LLMMessage
            {
                Role = "user",
                Content = request.Message
            });

            // 4. Build tool definitions from registry
            var allTools = _toolRegistry.GetAllTools().ToList();
            var toolDefinitions = allTools.Select(t => new ToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.InputSchema
            }).ToList();

            _logger.LogInformation("[ConversationAgent] {ToolCount} tools available", toolDefinitions.Count);

            // 5. RAG — embedding + recherche souvenirs + profil réel
            var userProfile = await BuildUserProfileAsync(ct);
            var relevantMemories = await BuildRelevantMemoriesAsync(request.Message, ct);

            // 6. Build system prompt with real data
            var systemPrompt = _promptBuilder.BuildSystemPrompt(
                userProfile,
                relevantMemories,
                new List<ToolCallDto>(),
                daemonConnected: _daemonClient.IsConnected,
                _llmRouter.ActiveProvider,
                voiceMode: request.VoiceMode
            );

            // 7. Build ToolExecutor callback
            async Task<string> ToolExecutor(string toolName, string argsJson)
            {
                _logger.LogInformation("[ConversationAgent] Executing tool: {ToolName}", toolName);

                var tool = _toolRegistry.GetTool(toolName);
                if (tool == null)
                {
                    _logger.LogWarning("[ConversationAgent] Tool not found: {ToolName}", toolName);
                    return JsonSerializer.Serialize(new { error = $"Tool '{toolName}' not found" });
                }

                JsonObject inputArgs;
                try
                {
                    inputArgs = string.IsNullOrWhiteSpace(argsJson) || argsJson == "{}"
                        ? new JsonObject()
                        : JsonNode.Parse(argsJson)?.AsObject() ?? new JsonObject();
                }
                catch
                {
                    inputArgs = new JsonObject();
                }

                try
                {
                    var result = await tool.ExecuteAsync(inputArgs, ct);
                    if (result.Success && result.Data != null)
                        return JsonSerializer.Serialize(result.Data);
                    return JsonSerializer.Serialize(new { error = result.Message ?? "Tool execution failed" });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ConversationAgent] Tool {ToolName} threw exception", toolName);
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            }

            // 8. Build LLM request
            var llmRequest = new LLMRequest
            {
                SystemPrompt = systemPrompt,
                Messages = messageHistory,
                Model = null, // Use default from router
                Temperature = 0.7f,
                Tools = toolDefinitions.Count > 0 ? toolDefinitions : null,
                ToolExecutor = toolDefinitions.Count > 0 ? ToolExecutor : null
            };

            // 9. Call LLM
            _logger.LogInformation("[ConversationAgent] Calling LLM with {MessageCount} messages", messageHistory.Count);
            var llmResponse = await _llmRouter.CompleteAsync(llmRequest, ct);

            _logger.LogInformation("[ConversationAgent] LLM response - Success: {Success}, Content length: {Length}",
                llmResponse.Success,
                llmResponse.Data?.Content?.Length ?? 0);

            if (!llmResponse.Success)
            {
                return ApiResponse<ChatResponse>.ErrorResponse(
                    llmResponse.Message ?? "LLM indisponible",
                    llmResponse.StatusCode);
            }

            if (string.IsNullOrEmpty(llmResponse.Data?.Content))
            {
                _logger.LogWarning("[ConversationAgent] LLM returned empty content");
            }

            // 10. Save assistant response + embedding for future RAG
            var assistantMessage = new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Role = MessageRole.Assistant,
                Content = llmResponse.Data!.Content,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Messages.AddAsync(assistantMessage, ct);

            // 11. Save all changes
            await _unitOfWork.SaveChangesAsync(ct);

            stopwatch.Stop();
            _logger.LogInformation(
                "Conversation processed in {ElapsedMs}ms - Session: {SessionId}, Provider: {Provider}",
                stopwatch.ElapsedMilliseconds,
                conversation.Id,
                _llmRouter.ActiveProvider);

            // 12. Return response
            return ApiResponse<ChatResponse>.SuccessResponse(new ChatResponse
            {
                Response = llmResponse.Data.Content,
                SessionId = conversation.Id,
                LlmProvider = _llmRouter.ActiveProvider,
                MemoryUsed = false, // TODO: implement memory
                ToolsCalled = null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process conversation");
            return ApiResponse<ChatResponse>.ErrorResponse("Internal error processing conversation", 500);
        }
    }

    // ── Phase 1 : Prepare (ApiResponse pattern — DB + prompt) ───────────────────
    public async Task<ApiResponse<StreamContext>> PrepareStreamAsync(ChatRequest request, CancellationToken ct = default)
    {
        try
        {
            // 1. Get or create conversation
            Conversation conversation;
            try
            {
                if (request.SessionId.HasValue)
                {
                    conversation = await _unitOfWork.Conversations.GetByIdAsync(request.SessionId.Value, ct);
                    if (conversation == null)
                    {
                        conversation = new Conversation
                        {
                            Id = request.SessionId.Value,
                            Type = ConversationType.Chat,
                            StartedAt = DateTime.UtcNow,
                            LlmProvider = _llmRouter.ActiveProvider
                        };
                        await _unitOfWork.Conversations.AddAsync(conversation, ct);
                    }
                }
                else
                {
                    conversation = new Conversation
                    {
                        Id = Guid.NewGuid(),
                        Type = ConversationType.Chat,
                        StartedAt = DateTime.UtcNow,
                        LlmProvider = _llmRouter.ActiveProvider
                    };
                    await _unitOfWork.Conversations.AddAsync(conversation, ct);
                }

                // 2. Save user message
                var userMessage = new Message
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    Role = MessageRole.User,
                    Content = request.Message,
                    CreatedAt = DateTime.UtcNow
                };
                await _unitOfWork.Messages.AddAsync(userMessage, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "[ConversationAgent/Prepare] DB unreachable — {Type}: {Msg}", dbEx.GetType().Name, dbEx.Message);
                return ApiResponse<StreamContext>.ErrorResponse($"Base de données inaccessible: {dbEx.Message}", 503);
            }

            // 3. Build message history
            var recentMessages = await _unitOfWork.Messages.GetByConversationIdAsync(conversation.Id, ct);
            var messageHistory = recentMessages.TakeLast(9).Select(m => new LLMMessage
            {
                Role = m.Role.ToString().ToLower(),
                Content = m.Content
            }).ToList();
            messageHistory.Add(new LLMMessage { Role = "user", Content = request.Message });

            // 4. Build tools
            var allTools = _toolRegistry.GetAllTools().ToList();
            var toolDefinitions = allTools.Select(t => new ToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.InputSchema
            }).ToList();

            // 5. Build system prompt (with RAG + voice mode)
            var userProfileStream = await BuildUserProfileAsync(ct);
            var memoriesStream = await BuildRelevantMemoriesAsync(request.Message, ct);
            var systemPrompt = _promptBuilder.BuildSystemPrompt(
                userProfileStream,
                memoriesStream,
                new List<ToolCallDto>(),
                daemonConnected: _daemonClient.IsConnected,
                _llmRouter.ActiveProvider,
                voiceMode: request.VoiceMode);

            // 6. Build LLM request
            var llmRequest = new LLMRequest
            {
                SystemPrompt = systemPrompt,
                Messages = messageHistory,
                Temperature = 0.7f,
                Tools = toolDefinitions.Count > 0 ? toolDefinitions : null
            };

            _logger.LogInformation("[ConversationAgent/Prepare] Session {Sid} — {Tools} tools", conversation.Id, toolDefinitions.Count);

            return ApiResponse<StreamContext>.SuccessResponse(new StreamContext
            {
                SessionId = conversation.Id,
                ConversationId = conversation.Id,
                LlmRequest = llmRequest
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConversationAgent/Prepare] Failed");
            return ApiResponse<StreamContext>.ErrorResponse($"Erreur préparation stream: {ex.Message}");
        }
    }

    // ── Phase 2 : Stream LLM (yield chunks + save at end) ────────────────────────
    public async IAsyncEnumerable<string> StreamLLMAsync(
        StreamContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var fullResponse = new StringBuilder();
        var channel = Channel.CreateUnbounded<string>();

        var streamTask = _llmRouter.StreamAsync(context.LlmRequest, async chunk =>
        {
            fullResponse.Append(chunk);
            await channel.Writer.WriteAsync(chunk, ct);
        }, ct).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                // Unwrap AggregateException so the channel propagates the inner exception directly
                var inner = t.Exception?.InnerException ?? t.Exception;
                channel.Writer.Complete(inner);
            }
            else
            {
                channel.Writer.Complete();
            }
        }, TaskScheduler.Default);

        await foreach (var chunk in channel.Reader.ReadAllAsync(ct))
        {
            yield return chunk;
        }

        await streamTask;

        // Save assistant response (best-effort — don't crash the stream)
        try
        {
            var assistantMessage = new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = context.ConversationId,
                Role = MessageRole.Assistant,
                Content = fullResponse.ToString(),
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Messages.AddAsync(assistantMessage, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("[ConversationAgent/Stream] Done — {Chars} chars", fullResponse.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConversationAgent/Stream] Failed to save assistant message");
        }
    }

    // ── Legacy combined (used by VoiceController HTTP) ────────────────────────────
    public async IAsyncEnumerable<string> StreamAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var prepResult = await PrepareStreamAsync(request, ct);
        if (!prepResult.Success || prepResult.Data == null)
        {
            _logger.LogError("[ConversationAgent/Stream] Prepare failed: {Msg}", prepResult.Message);
            yield break;
        }

        await foreach (var chunk in StreamLLMAsync(prepResult.Data, ct))
        {
            yield return chunk;
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Build user profile from database or fallback to default.
    /// TODO: Multi-user — load from authenticated user context instead of default profile
    /// </summary>
    private async Task<Dictionary<string, string>> BuildUserProfileAsync(CancellationToken ct)
    {
        try
        {
            var profiles = await _unitOfWork.UserProfile.GetAllAsync(ct);
            var profileDict = profiles.ToDictionary(p => p.Key, p => p.Value);
            if (profileDict.Count > 0)
            {
                _logger.LogInformation("[ConversationAgent] Loaded {Count} profile keys", profileDict.Count);
                return profileDict;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ConversationAgent] Failed to load user profile, using fallback");
        }

        // Fallback default — TODO: replace with auth context in multi-user scenario
        return new Dictionary<string, string> { ["name"] = "Utilisateur" };
    }

    /// <summary>
    /// Search relevant memories via RAG (embedding + pgvector similarity search)
    /// </summary>
    private async Task<List<MemoryVector>> BuildRelevantMemoriesAsync(string message, CancellationToken ct)
    {
        try
        {
            var embeddingResponse = await _embeddingService.GenerateEmbeddingAsync(message, ct);
            if (embeddingResponse.Success && embeddingResponse.Data?.Length > 0)
            {
                var memories = await _unitOfWork.Memory.SearchSimilarAsync(embeddingResponse.Data, 5, ct);
                var memoryList = memories.ToList();
                _logger.LogInformation("[ConversationAgent] RAG: {MemoryCount} memories found", memoryList.Count);
                return memoryList;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ConversationAgent] RAG failed, continuing without memories");
        }
        return new List<MemoryVector>();
    }
}
