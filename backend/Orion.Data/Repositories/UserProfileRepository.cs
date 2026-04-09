using Orion.Core.Entities;
using Orion.Core.Interfaces.Repositories;
using Orion.Data.Context;

namespace Orion.Data.Repositories;

public class UserProfileRepository : GenericRepository<UserProfile, string>, IUserProfileRepository
{
    public UserProfileRepository(OrionDbContext context) : base(context)
    {
    }
}
