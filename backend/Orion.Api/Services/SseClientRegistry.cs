using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orion.Api.Services;

/// <summary>
/// Les clients SSE connectes, et la diffusion vers eux. Singleton : un flux SSE vit des heures,
/// la liste doit survivre aux requetes et rester atteignable par les services d'arriere-plan.
/// </summary>
public class SseClientRegistry
{
    private readonly ConcurrentDictionary<string, HttpResponse> _clients = new();
    private readonly ILogger<SseClientRegistry> _logger;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // Enums en TEXTE, comme ChatController. Diverger ici envoie "state":1 au lieu de
        // "state":"ok" : le front retombe sur la couleur neutre, SANS erreur visible.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SseClientRegistry(ILogger<SseClientRegistry> logger) => _logger = logger;

    public int Count => _clients.Count;

    public void Add(string clientId, HttpResponse response) => _clients.TryAdd(clientId, response);

    public void Remove(string clientId) => _clients.TryRemove(clientId, out _);

    /// <summary>
    /// Envoie un evenement a UN client. Leve si la connexion est morte — a l appelant de decider.
    /// </summary>
    public static async Task SendAsync(HttpResponse response, string eventName, object data)
    {
        var json = JsonSerializer.Serialize(data, Json);
        await response.WriteAsync($"event: {eventName}\n");
        await response.WriteAsync($"data: {json}\n\n");
        await response.Body.FlushAsync();
    }

    /// <summary>
    /// Diffuse a tous, et retire les connexions mortes au passage.
    ///
    /// Un client ferme son onglet sans prevenir : l ecriture echoue, et sans ce nettoyage la
    /// liste grossirait indefiniment, chaque diffusion payant des tentatives sur des
    /// connexions fantomes.
    /// </summary>
    public async Task<int> BroadcastAsync(string eventName, object data)
    {
        var dead = new List<string>();

        foreach (var (clientId, response) in _clients)
        {
            try { await SendAsync(response, eventName, data); }
            catch { dead.Add(clientId); }
        }

        foreach (var id in dead) _clients.TryRemove(id, out _);

        if (dead.Count > 0)
            _logger.LogDebug("[SSE] {Dead} connexion(s) fermee(s) retiree(s)", dead.Count);

        return _clients.Count;
    }
}