using Microsoft.Extensions.Logging;
using Moq;
using Orion.Business.Services;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;
using System.Text.Json;

namespace Orion.Tests.Services;

public class AuditServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IAuditLogRepository> _mockAuditRepo;
    private readonly Mock<ILogger<AuditService>> _mockLogger;
    private readonly AuditService _service;

    public AuditServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockAuditRepo = new Mock<IAuditLogRepository>();
        _mockLogger = new Mock<ILogger<AuditService>>();

        _mockUnitOfWork.Setup(x => x.AuditLogs).Returns(_mockAuditRepo.Object);
        _mockUnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _service = new AuditService(_mockUnitOfWork.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task LogAsync_Should_Create_AuditLog_With_Correct_Data()
    {
        // Arrange
        var entityType = "Conversation";
        var entityId = Guid.NewGuid().ToString();
        var action = "Create";
        var metadata = "{ \"ip\": \"127.0.0.1\" }";

        // Act
        await _service.LogAsync(entityType, entityId, action, metadata: metadata);

        // Assert
        _mockAuditRepo.Verify(x => x.AddAsync(
            It.Is<AuditLog>(log =>
                log.EntityType == entityType &&
                log.EntityId == entityId &&
                log.Action == action &&
                log.Metadata == metadata &&
                log.Success == true),
            It.IsAny<CancellationToken>()),
            Times.Once);

        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogEntityCreateAsync_Should_Serialize_Entity_To_NewValues()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Chat,
            Summary = "Test conversation"
        };

        // Act
        await _service.LogEntityCreateAsync(conversation);

        // Assert
        _mockAuditRepo.Verify(x => x.AddAsync(
            It.Is<AuditLog>(log =>
                log.EntityType == "Conversation" &&
                log.EntityId == conversation.Id.ToString() &&
                log.Action == "Create" &&
                log.OldValues == null &&
                log.NewValues != null &&
                log.NewValues.Contains("Test conversation")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LogEntityUpdateAsync_Should_Serialize_Both_Entities()
    {
        // Arrange
        var oldConv = new Conversation { Id = Guid.NewGuid(), Summary = "Old" };
        var newConv = new Conversation { Id = oldConv.Id, Summary = "New" };

        // Act
        await _service.LogEntityUpdateAsync(oldConv, newConv);

        // Assert
        _mockAuditRepo.Verify(x => x.AddAsync(
            It.Is<AuditLog>(log =>
                log.Action == "Update" &&
                log.OldValues != null &&
                log.NewValues != null &&
                log.OldValues.Contains("Old") &&
                log.NewValues.Contains("New")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LogEntityDeleteAsync_Should_Serialize_Entity_To_OldValues()
    {
        // Arrange
        var conversation = new Conversation { Id = Guid.NewGuid(), Summary = "To Delete" };

        // Act
        await _service.LogEntityDeleteAsync(conversation);

        // Assert
        _mockAuditRepo.Verify(x => x.AddAsync(
            It.Is<AuditLog>(log =>
                log.Action == "Delete" &&
                log.OldValues != null &&
                log.NewValues == null &&
                log.OldValues.Contains("To Delete")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LogToolCallAsync_Should_Include_Input_Output_In_Metadata()
    {
        // Arrange
        var toolName = "get_shiftstar_stats";
        var input = "{ \"metric\": \"active_users\" }";
        var output = "{ \"count\": 42 }";

        // Act
        await _service.LogToolCallAsync(toolName, input, output, TimeSpan.FromSeconds(1), true);

        // Assert
        _mockAuditRepo.Verify(x => x.AddAsync(
            It.Is<AuditLog>(log =>
                log.EntityType == "Tool" &&
                log.EntityId == toolName &&
                log.Action == "ToolCall" &&
                log.Metadata != null &&
                log.Metadata.Contains("Input") &&
                log.Metadata.Contains("Output") &&
                log.Success == true),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LogAsync_When_Exception_Occurs_Should_Not_Throw()
    {
        // Arrange
        _mockAuditRepo.Setup(x => x.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act - Should not throw
        var exception = await Record.ExceptionAsync(async () =>
            await _service.LogAsync("Test", "123", "Action"));

        // Assert
        Assert.Null(exception);
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to create audit log")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void GetCorrelationId_When_Not_Set_Should_Return_New_Guid()
    {
        // Act
        var result = _service.GetCorrelationId();

        // Assert
        Assert.NotNull(result);
        Assert.True(Guid.TryParse(result, out _));
    }

    [Fact]
    public void SetCorrelationId_Should_Set_CorrelationId()
    {
        // Arrange
        var correlationId = "test-correlation-id";

        // Act
        _service.SetCorrelationId(correlationId);
        var result = _service.GetCorrelationId();

        // Assert
        Assert.Equal(correlationId, result);
    }
}
