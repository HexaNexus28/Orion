using Orion.Core.Entities;
using Orion.Core.Interfaces.Repositories;
using Orion.Data.Context;

namespace Orion.Data.Repositories;

public class ConversationRepository : GenericRepository<Conversation, Guid>, IConversationRepository
{
    public ConversationRepository(OrionDbContext context) : base(context)
    {
    }
}
