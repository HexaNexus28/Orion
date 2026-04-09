using Orion.Core.Entities;

namespace Orion.Core.Interfaces.Repositories;

public interface IBehaviorPatternRepository : IGenericRepository<BehaviorPattern, Guid>
{
    Task<IEnumerable<BehaviorPattern>> GetRecentPatternsAsync(int count, CancellationToken ct = default);
    Task<IEnumerable<BehaviorPattern>> GetByPatternTypeAsync(string patternType, CancellationToken ct = default);
    Task<IEnumerable<BehaviorPattern>> GetSinceAsync(DateTime since, CancellationToken ct = default);
}
