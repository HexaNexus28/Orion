using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orion.Api.Services;

/// <summary>
/// Les clients SSE connectes, et la diffusion vers eux.
///
/// POURQUOI CE SERVICE EXISTE. La liste vivait dans un champ `private static` de
/// ProactiveNotificationController, avec la boucle de diffusion recopiee a trois endroits du
/// meme fichier. Tant que seul ce controleur diffusait, ca tenait. Des qu'un service
/// d'arriere-plan doit pousser quelque chose — les cartes permanentes du HUD — il faudrait soit
/// atteindre un champ statique prive depuis l'exterieur, soit tenir une seconde liste. Les deux
/// sont des impasses.
///
/// Singleton : la liste doit survivre aux requetes, un flux SSE vit des heures.
/// </summary>
public class SseClientRegistry
{
    private readonly ConcurrentDictionary<string, HttpResponse> _clients = new();
    private readonly ILogger<SseClientRegistry> _logger;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // Enums en TEXTE, comme dans ChatController. Constate en production le 2026-08-26 : la
        // meme carte partait en "state":"ok" par le flux de chat et en "state":1 par ce flux-ci.
        // Le front lisait alors une valeur inconnue et retombait sur la couleur neutre — un
        // panneau permanent affiche en gris au lieu de vert, SANS la moindre erreur.
        //
        // Deux endroits qui serialisent la meme chose finissent toujours par diverger : c est
        // arrive ici entre l ecriture du contrat et sa verification, le meme jour.
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