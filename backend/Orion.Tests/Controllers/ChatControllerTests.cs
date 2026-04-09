using Moq;
using Orion.Api.Controllers;
using Orion.Core.DTOs;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Orion.Tests.Controllers;

public class ChatControllerTests
{
    private readonly Mock<IChatService> _mockService;
    private readonly ChatController _controller;

    public ChatControllerTests()
    {
        _mockService = new Mock<IChatService>();
        _controller = new ChatController(_mockService.Object);
    }

    [Fact]
    public async Task Chat_When_Success_Returns_200()
    {
        // Arrange
        var request = new ChatRequest { Message = "Hello", SessionId = Guid.NewGuid() };
        var response = ApiResponse<ChatResponse>.SuccessResponse(new ChatResponse
        {
            Response = "Hi!",
            SessionId = request.SessionId.Value,
            LlmProvider = LLMProvider.Ollama
        });

        _mockService.Setup(x => x.SendMessageAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.Chat(request, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<ChatResponse>>(objectResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(200, apiResponse.StatusCode);
    }

    [Fact]
    public async Task Chat_When_NotFound_Returns_404()
    {
        // Arrange
        var request = new ChatRequest { Message = "Hello", SessionId = Guid.NewGuid() };
        var response = ApiResponse<ChatResponse>.NotFoundResponse("Session not found");

        _mockService.Setup(x => x.SendMessageAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.Chat(request, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetConversation_When_Exists_Returns_200()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var response = ApiResponse<ChatResponse>.SuccessResponse(new ChatResponse
        {
            Response = "Conversation found",
            SessionId = sessionId
        });

        _mockService.Setup(x => x.GetConversationAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetConversation(sessionId, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetConversations_Returns_200_With_Paged_Results()
    {
        // Arrange
        var response = ApiResponse<List<ConversationSummaryDto>>.SuccessResponse(new List<ConversationSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Summary = "Test", MessageCount = 5 }
        });

        _mockService.Setup(x => x.GetConversationsAsync(1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetConversations(1, 20, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
    }
}
