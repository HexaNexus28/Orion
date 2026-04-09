using Microsoft.EntityFrameworkCore;
using Orion.Core.Entities;
using Orion.Core.Interfaces.Repositories;
using Orion.Data.Context;

namespace Orion.Data.Repositories;

public class MemoryRepository : GenericRepository<MemoryVector, Guid>, IMemoryRepository
{
    public MemoryRepository(OrionDbContext context) : base(context)
    {
    }

    // Override to exclude embedding column (pgvector type not supported by EF Core)
    public override async Task<IEnumerable<MemoryVector>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Set<MemoryVector>()
            .AsNoTracking()
            .Select(m => new MemoryVector
            {
                Id = m.Id,
                Content = m.Content,
                Source = m.Source,
                Importance = m.Importance,
                CreatedAt = m.CreatedAt,
                LastAccessed = m.LastAccessed
                // Embedding excluded - handled via raw SQL
            })
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<MemoryVector>> SearchSimilarAsync(float[] embedding, int topK = 5, CancellationToken ct = default)
    {
        // pgvector cosine similarity search using raw SQL
        // Explicitly exclude embedding column (pgvector type not supported by EF Core)
        var embeddingString = $"[{string.Join(",", embedding)}]";
        
        var sql = $@"
            SELECT id, content, source, importance, created_at, last_accessed 
            FROM memory_vectors 
            ORDER BY embedding <=> '{embeddingString}'::vector 
            LIMIT {topK}";

        // Note: In production, use parameterized queries or EF Core pgvector extension
        return await _dbSet
            .FromSqlRaw(sql)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<MemoryVector>> GetBySourceAsync(string source, CancellationToken ct = default)
    {
        // Explicit projection to exclude embedding column (pgvector type)
        return await _dbSet
            .AsNoTracking()
            .Where(m => m.Source == source)
            .OrderByDescending(m => m.Importance)
            .Select(m => new MemoryVector
            {
                Id = m.Id,
                Content = m.Content,
                Source = m.Source,
                Importance = m.Importance,
                CreatedAt = m.CreatedAt,
                LastAccessed = m.LastAccessed
            })
            .ToListAsync(ct);
    }
}
