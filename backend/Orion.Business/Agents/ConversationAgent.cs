using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Tools;
using Orion.Business.LLM;

namespace Orion.Business.Agents;

public class ConversationAgent : IConversationAgent
{
    private readonly ILLMRouter _llmRouter;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PromptBuilder _promptBuilder;
    private readonly IToolRegistry _toolRegistry;
    private readonly IDaemonClient _daemonClient;
    private readonly ILogger<ConversationAgent> _logger;

    public ConversationAgent(
        ILLMRouter llmRouter,
        IUnitOfWork unitOfWork,
        PromptBuilder promptBuilder,
        IToolRegistry toolRegistry,
        IDaemonClient daemonClient,
        ILogger<ConversationAgent> logger)
    {
        _llmRouter = llmRouter;
        _unitOfWork = unitOfWork;
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

            // 5. Build system prompt
            var systemPrompt = _promptBuilder.BuildSystemPrompt(
                new Dictionary<string, string> { ["name"] = "Yawo" },
                new List<MemoryVector>(),
                new List<ToolCallDto>(),
                daemonConnected: _daemonClient.IsConnected,
                _llmRouter.ActiveProvider
            );

            // 6. Build ToolExecutor callback
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

            // 7. Build LLM request
            var llmRequest = new LLMRequest
            {
                SystemPrompt = systemPrompt,
                Messages = messageHistory,
                Model = null, // Use default from router
                Temperature = 0.7f,
                Tools = toolDefinitions.Count > 0 ? toolDefinitions : null,
                ToolExecutor = toolDefinitions.Count > 0 ? ToolExecutor : null
            };

            // 8. Call LLM
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

            // 9. Save assistant response
            var assistantMessage = new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Role = MessageRole.Assistant,
                Content = llmResponse.Data!.Content,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Messages.AddAsync(assistantMessage, ct);

            // 10. Save all changes
            await _unitOfWork.SaveChangesAsync(ct);

            stopwatch.Stop();
            _logger.LogInformation(
                "Conversation processed in {ElapsedMs}ms - Session: {SessionId}, Provider: {Provider}",
                stopwatch.ElapsedMilliseconds,
                conversation.Id,
                _llmRouter.ActiveProvider);

            // 11. Return response
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
}
