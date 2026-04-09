using System.Diagnostics;
using System.Text.Json;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class OpenAppAction : IAction
{
    private readonly DaemonOptions _options;

    public OpenAppAction(DaemonOptions options)
    {
        _options = options;
    }

    public string Name => "open_app";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        var app = payload.GetProperty("application").GetString();
        if (string.IsNullOrEmpty(app))
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, "Missing application"));
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = app,
                UseShellExecute = true
            };

            var process = Process.Start(psi);

            var data = new
            {
                application = app,
                processId = process?.Id,
                started = true
            };

            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }
}
