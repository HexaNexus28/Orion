using Microsoft.EntityFrameworkCore;
using Orion.Core.Entities;
using Orion.Core.Interfaces.Repositories;
using Orion.Data.Context;

namespace Orion.Data.Repositories;

public class MessageRepository : GenericRepository<Message, Guid>, IMessageRepository
{
    public MessageRepository(OrionDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Message>> GetByConversationIdAsync(Guid conversationId, CancellationToken ct = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
    }
}
