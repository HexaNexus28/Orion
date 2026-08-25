using System.Net.WebSockets;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Daemon;

public interface IDaemonClient
{
    Task<ApiResponse<DaemonActionResponse>> SendActionAsync(DaemonActionRequest action, CancellationToken ct = default);
    Task<ApiResponse<byte[]?>> SendBinaryActionAsync(DaemonActionRequest action, CancellationToken ct = default);
    Task RegisterConnectionAsync(string machineName, WebSocket webSocket, CancellationToken ct = default);
    bool IsConnected { get; }

    /// <summary>
    /// Levé quand un daemon vient de se connecter — c'est-à-dire quand le PC se rallume.
    ///
    /// Par événement et non par sondage : un `BackgroundService` qui interroge `IsConnected`
    /// toutes les trente secondes ajoute jusqu'à trente secondes de latence au réveil et tourne
    /// pour rien vingt-trois heures par jour. Porte le nom de la machine.
    /// </summary>
    event Action<string>? DaemonConnected;
    string MachineName { get; }
}
