using Microsoft.Extensions.Logging;
using Moq;
using Orion.Business.Services;
using Orion.Core.DTOs;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

namespace Orion.Tests.Services;

public class ChatServiceTests
{
    private readonly Mock<IConversationAgent> _mockAgent;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly VoiceNotificationService _voiceNotification;
    private readonly Mock<ILogger<ChatService>> _mockLogger;
    private readonly ChatService _service;

    public ChatServiceTests()
    {
        _mockAgent = new Mock<IConversationAgent>();
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockAuditService = new Mock<IAuditService>();
        _voiceNotification = new VoiceNotificationService(
            new Mock<IDaemonClient>().Object,
            new Mock<ILogger<VoiceNotificationService>>().Object);
        _mockLogger = new Mock<ILogger<ChatService>>();
        _service = new ChatService(_mockAgent.Object, _mockUnitOfWork.Object, _mockAuditService.Object, _voiceNotification, _mockLogger.Object);
    }

    [Fact]
    public async Task SendMessageAsync_Should_Call_Agent_ProcessAsync()
    {
        // Arrange
        var request = new ChatRequest { Message = "Hello", SessionId = Guid.NewGuid() };
        var expectedResponse = ApiResponse<ChatResponse>.SuccessResponse(new ChatResponse
        {
            Response = "Hi there!",
            SessionId = request.SessionId.Value,
            LlmProvider = LLMProvider.Ollama
        });

        _mockAgent.Setup(x => x.ProcessAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.SendMessageAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Hi there!", result.Data?.Response);
        _mockAgent.Verify(x => x.ProcessAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetConversationAsync_When_Conversation_Exists_Returns_Success()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var conversation = new Conversation
        {
            Id = sessionId,
            Type = ConversationType.Chat,
            StartedAt = DateTime.UtcNow,
            LlmProvider = LLMProvider.Ollama
        };

        _mockUnitOfWork.Setup(x => x.Conversations.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(x => x.Messages.GetByConversationIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Message>());

        // Act
        var result = await _service.GetConversationAsync(sessionId);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(sessionId, result.Data?.SessionId);
    }

    [Fact]
    public async Task GetConversationAsync_When_Conversation_NotFound_Returns_NotFound()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _mockUnitOfWork.Setup(x => x.Conversations.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.GetConversationAsync(sessionId);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task GetConversationsAsync_Returns_Paged_Results()
    {
        // Arrange
        var conversations = new List<Conversation>
        {
            new() { Id = Guid.NewGuid(), Summary = "Test 1", StartedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = Guid.NewGuid(), Summary = "Test 2", StartedAt = DateTime.UtcNow }
        };

        _mockUnitOfWork.Setup(x => x.Conversations.GetPagedAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<System.Linq.Expressions.Expression<System.Func<Conversation, bool>>?>(),
                It.IsAny<System.Linq.Expressions.Expression<System.Func<Conversation, object>>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((conversations, 2));

        _mockUnitOfWork.Setup(x => x.Messages.CountAsync(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<Message, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _service.GetConversationsAsync(1, 20);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data?.Count);
    }
}
