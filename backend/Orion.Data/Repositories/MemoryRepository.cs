using System.Globalization;
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

    /// <summary>
    /// Recherche par similarité cosinus (pgvector). Les souvenirs sans vecteur sont exclus :
    /// sinon ils remontent en tête, `NULL` étant considéré comme la plus petite distance.
    /// </summary>
    public async Task<IEnumerable<MemoryVector>> SearchSimilarAsync(float[] embedding, int topK = 5, CancellationToken ct = default)
    {
        // Requête PARAMÉTRÉE — l'ancienne concaténait le vecteur dans la chaîne SQL.
        const string sql = @"
            SELECT id, content, source, importance, created_at, last_accessed
            FROM memory_vectors
            WHERE embedding IS NOT NULL
            ORDER BY embedding <=> CAST({0} AS vector)
            LIMIT {1}";

        return await _dbSet
            .FromSqlRaw(sql, Format(embedding), topK)
            .ToListAsync(ct);
    }

    public async Task SaveEmbeddingAsync(Guid id, float[] embedding, CancellationToken ct = default)
    {
        if (embedding.Length == 0) return;

        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE memory_vectors SET embedding = CAST({0} AS vector) WHERE id = {1}",
            new object[] { Format(embedding), id },
            ct);
    }

    /// <summary>Représentation textuelle attendue par pgvector : [0.1,0.2,...].</summary>
    private static string Format(float[] embedding)
        => "[" + string.Join(",", embedding.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]";

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
