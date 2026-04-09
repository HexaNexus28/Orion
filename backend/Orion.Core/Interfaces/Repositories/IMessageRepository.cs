using Orion.Core.Entities;

namespace Orion.Core.Interfaces.Repositories;

public interface IMessageRepository : IGenericRepository<Message, Guid>
{
    Task<IEnumerable<Message>> GetByConversationIdAsync(Guid conversationId, CancellationToken ct = default);
}
