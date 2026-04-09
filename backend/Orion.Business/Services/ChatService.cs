using Microsoft.Extensions.Logging;
using Orion.Core.DTOs;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

public class ChatService : IChatService
{
    private readonly IConversationAgent _conversationAgent;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly VoiceNotificationService _voiceNotification;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IConversationAgent conversationAgent,
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        VoiceNotificationService voiceNotification,
        ILogger<ChatService> logger)
    {
        _conversationAgent = conversationAgent;
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _voiceNotification = voiceNotification;
        _logger = logger;
    }

    public async Task<ApiResponse<ChatResponse>> SendMessageAsync(ChatRequest request, CancellationToken ct = default)
    {
        // Set correlation ID for this request
        _auditService.SetCorrelationId(Guid.NewGuid().ToString("N"));
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            var response = await _conversationAgent.ProcessAsync(request, ct);
            
            stopwatch.Stop();
            
            // Audit the chat message + Send TTS notification
            if (response.Success && response.Data != null)
            {
                await _auditService.LogAsync(
                    entityType: "ChatMessage",
                    entityId: response.Data.SessionId.ToString(),
                    action: "SendMessage",
                    newValues: System.Text.Json.JsonSerializer.Serialize(new { request.Message, response.Data.Response }),
                    metadata: $"{{ \"provider\": \"{response.Data.LlmProvider}\", \"durationMs\": {stopwatch.ElapsedMilliseconds} }}",
                    duration: stopwatch.Elapsed,
                    success: true,
                    ct: ct);

                // TTS déclenché par le frontend via POST /api/voice/synthesize
            }
            
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error sending message");
            
            await _auditService.LogAsync(
                entityType: "ChatMessage",
                entityId: request.SessionId?.ToString() ?? "unknown",
                action: "SendMessage",
                newValues: System.Text.Json.JsonSerializer.Serialize(new { request.Message }),
                duration: stopwatch.Elapsed,
                success: false,
                errorMessage: ex.Message,
                ct: ct);
            
            throw;
        }
    }

    public async Task<ApiResponse<ChatResponse>> GetConversationAsync(Guid sessionId, CancellationToken ct = default)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(sessionId, ct);
        
        if (conversation == null)
        {
            return ApiResponse<ChatResponse>.NotFoundResponse("Conversation introuvable");
        }

        var messages = await _unitOfWork.Messages.GetByConversationIdAsync(sessionId, ct);
        var lastMessage = messages.LastOrDefault();

        return ApiResponse<ChatResponse>.SuccessResponse(new ChatResponse
        {
            Response = lastMessage?.Content ?? "",
            SessionId = sessionId,
            LlmProvider = conversation.LlmProvider ?? LLMProvider.Ollama,
            MemoryUsed = false,
            ToolsCalled = null
        });
    }

    public async Task<ApiResponse<List<ConversationSummaryDto>>> GetConversationsAsync(
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var (conversations, totalCount) = await _unitOfWork.Conversations.GetPagedAsync(
            page, pageSize, 
            filter: null, 
            orderBy: c => c.StartedAt, 
            ascending: false, ct);

        var summaries = new List<ConversationSummaryDto>();
        
        foreach (var conv in conversations)
        {
            var messageCount = await _unitOfWork.Messages.CountAsync(m => m.ConversationId == conv.Id, ct);
            var lastMessage = await _unitOfWork.Messages.FirstOrDefaultAsync(
                m => m.ConversationId == conv.Id, ct);

            summaries.Add(new ConversationSummaryDto
            {
                Id = conv.Id,
                Summary = conv.Summary,
                StartedAt = conv.StartedAt,
                LastMessageAt = lastMessage?.CreatedAt,
                MessageCount = messageCount
            });
        }

        return ApiResponse<List<ConversationSummaryDto>>.SuccessResponse(summaries);
    }

    /// <summary>
    /// Stream a message response word by word (Server-Sent Events)
    /// Phase 4: Real-time streaming for voice interface
    /// </summary>
    public async IAsyncEnumerable<string> StreamMessageAsync(
        ChatRequest request, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        _auditService.SetCorrelationId(Guid.NewGuid().ToString("N"));
        _logger.LogInformation("[ChatService] Streaming message for session {SessionId}", request.SessionId);

        // For now, simulate streaming by processing normally and yielding words
        // In production, this should connect to ILLMClient.StreamAsync for true streaming
        var response = await _conversationAgent.ProcessAsync(request, ct);

        if (response.Success && response.Data != null)
        {
            var content = response.Data.Response;
            var words = content.Split(' ');

            foreach (var word in words)
            {
                yield return word + " ";
                await Task.Delay(50, ct); // Simulate typing delay
            }

            // Audit the streamed message
            var metadata = System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionId = request.SessionId,
                wordCount = words.Length,
                duration = words.Length * 50
            });
            await _auditService.LogAsync(
                entityType: "ChatMessageStream",
                entityId: Guid.NewGuid().ToString(),
                action: "Stream",
                metadata: metadata
            );
        }
        else
        {
            yield return response.Message ?? "Error processing message";
        }
    }
}
