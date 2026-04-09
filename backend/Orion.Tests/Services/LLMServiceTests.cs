using Microsoft.Extensions.Logging;
using Moq;
using Orion.Business.Services;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Services;

namespace Orion.Tests.Services;

public class LLMServiceTests
{
    private readonly Mock<ILLMRouter> _mockRouter;
    private readonly Mock<ILogger<LLMService>> _mockLogger;
    private readonly LLMService _service;

    public LLMServiceTests()
    {
        _mockRouter = new Mock<ILLMRouter>();
        _mockLogger = new Mock<ILogger<LLMService>>();
        _service = new LLMService(_mockRouter.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CompleteAsync_Should_Call_Router()
    {
        // Arrange
        var request = new LLMRequest
        {
            Messages = new List<LLMMessage> { new() { Role = "user", Content = "Hello" } }
        };

        var expectedResponse = ApiResponse<LLMResponse>.SuccessResponse(new LLMResponse
        {
            Content = "Response",
            Provider = LLMProvider.Ollama,
            Model = "kimi-k2"
        });

        _mockRouter.Setup(x => x.CompleteAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.CompleteAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Response", result.Data?.Content);
        _mockRouter.Verify(x => x.CompleteAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteWithPromptAsync_Should_Create_Request_And_Call_Router()
    {
        // Arrange
        var systemPrompt = "You are helpful";
        var userMessage = "Hello";

        var expectedResponse = ApiResponse<LLMResponse>.SuccessResponse(new LLMResponse
        {
            Content = "Hi!",
            Provider = LLMProvider.Ollama,
            Model = "kimi-k2"
        });

        _mockRouter.Setup(x => x.CompleteAsync(It.Is<LLMRequest>(r =>
            r.SystemPrompt == systemPrompt &&
            r.Messages.Count == 1 &&
            r.Messages[0].Content == userMessage), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.CompleteWithPromptAsync(systemPrompt, userMessage);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Hi!", result.Data?.Content);
    }

    [Fact]
    public void GetActiveProvider_Should_Return_Router_ActiveProvider()
    {
        // Arrange
        _mockRouter.Setup(x => x.ActiveProvider).Returns(LLMProvider.Anthropic);

        // Act
        var result = _service.GetActiveProvider();

        // Assert
        Assert.Equal(LLMProvider.Anthropic, result);
    }

    [Fact]
    public async Task IsAvailableAsync_When_Router_Has_ActiveProvider_Returns_True()
    {
        // Arrange
        _mockRouter.Setup(x => x.ActiveProvider).Returns(LLMProvider.Ollama);

        // Act
        var result = await _service.IsAvailableAsync();

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task IsAvailableAsync_When_Router_Has_No_Provider_Returns_False()
    {
        // Arrange - When router has no active provider
        _mockRouter.Setup(x => x.ActiveProvider).Returns(LLMProvider.None);

        // Act
        var result = await _service.IsAvailableAsync();

        // Assert - None provider should return false
        Assert.True(result.Success);
        Assert.False(result.Data);
    }
}
