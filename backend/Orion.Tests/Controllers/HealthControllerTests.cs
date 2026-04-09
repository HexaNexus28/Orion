using Moq;
using Orion.Api.Controllers;
using Orion.Core.DTOs;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Orion.Tests.Controllers;

public class HealthControllerTests
{
    private readonly Mock<IHealthService> _mockHealthService;
    private readonly HealthController _controller;

    public HealthControllerTests()
    {
        _mockHealthService = new Mock<IHealthService>();
        _controller = new HealthController(_mockHealthService.Object);
    }

    [Fact]
    public void GetHealth_When_Ollama_Active_Returns_Healthy_With_Ollama()
    {
        // Arrange
        var response = ApiResponse<HealthCheckDto>.SuccessResponse(new HealthCheckDto
        {
            Status = "healthy",
            LlmProvider = "Ollama",
            Timestamp = DateTime.UtcNow
        });
        _mockHealthService.Setup(x => x.GetHealthStatus()).Returns(response);

        // Act
        var result = _controller.GetHealth();

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        
        var apiResponse = Assert.IsType<ApiResponse<HealthCheckDto>>(objectResult.Value);
        Assert.Equal("healthy", apiResponse.Data?.Status);
        Assert.Equal("Ollama", apiResponse.Data?.LlmProvider);
    }

    [Fact]
    public void GetHealth_When_Anthropic_Active_Returns_Healthy_With_Anthropic()
    {
        // Arrange
        var response = ApiResponse<HealthCheckDto>.SuccessResponse(new HealthCheckDto
        {
            Status = "healthy",
            LlmProvider = "Anthropic",
            Timestamp = DateTime.UtcNow
        });
        _mockHealthService.Setup(x => x.GetHealthStatus()).Returns(response);

        // Act
        var result = _controller.GetHealth();

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        
        var apiResponse = Assert.IsType<ApiResponse<HealthCheckDto>>(objectResult.Value);
        Assert.Equal("Anthropic", apiResponse.Data?.LlmProvider);
    }

    [Fact]
    public void GetHealth_When_No_Provider_Returns_Healthy_With_None()
    {
        // Arrange
        var response = ApiResponse<HealthCheckDto>.SuccessResponse(new HealthCheckDto
        {
            Status = "healthy",
            LlmProvider = "None",
            Timestamp = DateTime.UtcNow
        });
        _mockHealthService.Setup(x => x.GetHealthStatus()).Returns(response);

        // Act
        var result = _controller.GetHealth();

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        
        var apiResponse = Assert.IsType<ApiResponse<HealthCheckDto>>(objectResult.Value);
        Assert.Equal("None", apiResponse.Data?.LlmProvider);
    }
}
