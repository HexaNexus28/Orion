using Orion.Core.Entities;

namespace Orion.Core.Interfaces.Repositories;

public interface IMemoryRepository : IGenericRepository<MemoryVector, Guid>
{
    // Semantic search using pgvector cosine similarity
    Task<IEnumerable<MemoryVector>> SearchSimilarAsync(
        float[] embedding, int topK = 5, CancellationToken ct = default);
    
    Task<IEnumerable<MemoryVector>> GetBySourceAsync(
        string source, CancellationToken ct = default);
}
