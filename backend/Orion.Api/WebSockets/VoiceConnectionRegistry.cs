namespace Orion.Api.WebSockets;

/// <summary>
/// UN SEUL micro ecoute a la fois.
///
/// ORION n a qu un utilisateur, mais la PWA peut etre ouverte a plusieurs endroits : un onglet
/// oublie, le telephone, le PC. Observe le 2026-11-04 : trois clients connectes, deux conversations
/// en parallele, et chaque micro captait la voix d ORION jouee par l autre appareil. La protection
/// anti-echo ne peut rien : elle ne connait que le son joue par SON appareil.
///
/// Le dernier connecte prend la main : c est celui que l utilisateur vient d ouvrir.
/// </summary>
public class VoiceConnectionRegistry
{
    private readonly object _lock = new();
    private VoiceWebSocketHandler? _current;

    /// <summary>Installe la nouvelle connexion et rend celle qu elle remplace.</summary>
    public VoiceWebSocketHandler? Replace(VoiceWebSocketHandler handler)
    {
        lock (_lock)
        {
            var previous = _current;
            _current = handler;
            return previous;
        }
    }

    /// <summary>Sans effet si une connexion plus recente a deja pris la place.</summary>
    public void Release(VoiceWebSocketHandler handler)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_current, handler)) _current = null;
        }
    }
}
