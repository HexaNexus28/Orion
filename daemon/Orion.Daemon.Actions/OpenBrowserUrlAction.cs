using System.Diagnostics;
using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class OpenBrowserUrlAction : IAction
{
    public string Name => "open_url";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var url = payload.GetProperty("url").GetString();
        if (string.IsNullOrEmpty(url))
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, "Missing URL"));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "http" || uri.Scheme == "https"))
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, $"Invalid URL: {url}"));
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };

            Process.Start(psi);

            var data = new { url = url, opened = true };
            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }
}
