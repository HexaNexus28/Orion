using Microsoft.EntityFrameworkCore;
using Orion.Core.Entities;
using Orion.Core.Interfaces.Repositories;
using Orion.Data.Context;

namespace Orion.Data.Repositories;

public class BehaviorPatternRepository : GenericRepository<BehaviorPattern, Guid>, IBehaviorPatternRepository
{
    private readonly OrionDbContext _context;

    public BehaviorPatternRepository(OrionDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IEnumerable<BehaviorPattern>> GetRecentPatternsAsync(int count, CancellationToken ct = default)
    {
        return await _context.Set<BehaviorPattern>()
            .OrderByDescending(p => p.ObservedAt)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<BehaviorPattern>> GetByPatternTypeAsync(string patternType, CancellationToken ct = default)
    {
        return await _context.Set<BehaviorPattern>()
            .Where(p => p.PatternType == patternType)
            .OrderByDescending(p => p.ObservedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<BehaviorPattern>> GetSinceAsync(DateTime since, CancellationToken ct = default)
    {
        return await _context.Set<BehaviorPattern>()
            .Where(p => p.ObservedAt > since)
            .OrderByDescending(p => p.ObservedAt)
            .ToListAsync(ct);
    }
}
