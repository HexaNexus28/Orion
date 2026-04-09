namespace Orion.Core.Interfaces.Agents;

// Scaffold - to be fully implemented
public interface IMemoryAgent
{
    // Returns context: profile + relevant memories + recent messages
    Task<object> GetContextAsync(string message, CancellationToken ct = default);
    Task SaveMemoryAsync(string content, float[] embedding, CancellationToken ct = default);
}
