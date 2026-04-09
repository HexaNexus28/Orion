using Microsoft.EntityFrameworkCore;
using Orion.Data.Context;
using Orion.Data.Repositories;
using Orion.Core.Entities;
using Orion.Core.Enums;

namespace Orion.Tests.Repositories;

public class GenericRepositoryTests : IDisposable
{
    private readonly OrionDbContext _context;
    private readonly GenericRepositoryTestHelper _repository;

    public GenericRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<OrionDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new OrionDbContext(options);
        _repository = new GenericRepositoryTestHelper(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task AddAsync_Should_Add_Entity()
    {
        // Arrange
        var entity = new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Chat,
            StartedAt = DateTime.UtcNow
        };

        // Act
        var result = await _repository.AddAsync(entity);
        await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(entity, result);
        Assert.Equal(1, await _context.Conversations.CountAsync());
    }

    [Fact]
    public async Task GetByIdAsync_When_Exists_Returns_Entity()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new Conversation
        {
            Id = id,
            Type = ConversationType.Chat,
            StartedAt = DateTime.UtcNow
        };
        await _context.Conversations.AddAsync(entity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(id, result.Id);
    }

    [Fact]
    public async Task GetByIdAsync_When_Not_Exists_Returns_Null()
    {
        // Act
        var result = await _repository.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_Returns_All_Entities()
    {
        // Arrange
        await _context.Conversations.AddRangeAsync(
            new Conversation { Id = Guid.NewGuid(), Type = ConversationType.Chat, StartedAt = DateTime.UtcNow },
            new Conversation { Id = Guid.NewGuid(), Type = ConversationType.Briefing, StartedAt = DateTime.UtcNow }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task FindAsync_With_Predicate_Returns_Filtered_Results()
    {
        // Arrange
        await _context.Conversations.AddRangeAsync(
            new Conversation { Id = Guid.NewGuid(), Type = ConversationType.Chat, StartedAt = DateTime.UtcNow },
            new Conversation { Id = Guid.NewGuid(), Type = ConversationType.Briefing, StartedAt = DateTime.UtcNow }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.FindAsync(c => c.Type == ConversationType.Chat);

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task ExistsAsync_When_Exists_Returns_True()
    {
        // Arrange
        var id = Guid.NewGuid();
        await _context.Conversations.AddAsync(new Conversation { Id = id, Type = ConversationType.Chat, StartedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.ExistsAsync(c => c.Id == id);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_When_Not_Exists_Returns_False()
    {
        // Act
        var result = await _repository.ExistsAsync(c => c.Id == Guid.NewGuid());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CountAsync_Returns_Total_Count()
    {
        // Arrange
        await _context.Conversations.AddRangeAsync(
            new Conversation { Id = Guid.NewGuid(), Type = ConversationType.Chat, StartedAt = DateTime.UtcNow },
            new Conversation { Id = Guid.NewGuid(), Type = ConversationType.Chat, StartedAt = DateTime.UtcNow },
            new Conversation { Id = Guid.NewGuid(), Type = ConversationType.Briefing, StartedAt = DateTime.UtcNow }
        );
        await _context.SaveChangesAsync();

        // Act
        var total = await _repository.CountAsync();
        var chatOnly = await _repository.CountAsync(c => c.Type == ConversationType.Chat);

        // Assert
        Assert.Equal(3, total);
        Assert.Equal(2, chatOnly);
    }

    [Fact]
    public async Task Remove_Should_Delete_Entity()
    {
        // Arrange
        var entity = new Conversation { Id = Guid.NewGuid(), Type = ConversationType.Chat, StartedAt = DateTime.UtcNow };
        await _context.Conversations.AddAsync(entity);
        await _context.SaveChangesAsync();

        // Act
        _repository.Remove(entity);
        await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(0, await _context.Conversations.CountAsync());
    }

    // Helper class to test GenericRepository
    private class GenericRepositoryTestHelper : GenericRepository<Conversation, Guid>
    {
        public GenericRepositoryTestHelper(OrionDbContext context) : base(context) { }
    }
}
