using Orion.Core.Entities;

namespace Orion.Core.Interfaces.Repositories;

public interface IMemoryRepository : IGenericRepository<MemoryVector, Guid>
{
    // Semantic search using pgvector cosine similarity
    Task<IEnumerable<MemoryVector>> SearchSimilarAsync(
        float[] embedding, int topK = 5, CancellationToken ct = default);
    
    Task<IEnumerable<MemoryVector>> GetBySourceAsync(
        string source, CancellationToken ct = default);

    /// <summary>
    /// Écrit le vecteur d'un souvenir. EF Core ignore la colonne `embedding` (type pgvector),
    /// donc un simple `AddAsync` la laisse à NULL — et un souvenir sans vecteur est invisible
    /// pour la recherche sémantique. Cette moitié du contrat manquait : seule la lecture avait
    /// été implémentée en SQL brut.
    /// </summary>
    Task SaveEmbeddingAsync(Guid id, float[] embedding, CancellationToken ct = default);
}
