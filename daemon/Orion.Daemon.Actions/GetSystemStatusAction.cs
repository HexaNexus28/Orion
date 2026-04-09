using System.Diagnostics;
using System.Text.Json;
using Orion.Daemon.Core.Entities;
using Orion.Daemon.Core.Interfaces;

namespace Orion.Daemon.Actions;

public class GetSystemStatusAction : IAction
{
    public string Name => "system_status";

    public Task<DaemonResponse> ExecuteAsync(JsonElement payload, string correlationId)
    {
        try
        {
            var proc = Process.GetCurrentProcess();

            var data = new
            {
                machineName = Environment.MachineName,
                userName = Environment.UserName,
                osVersion = Environment.OSVersion.ToString(),
                processorCount = Environment.ProcessorCount,
                workingSetMb = proc.WorkingSet64 / 1024 / 1024,
                uptimeMinutes = Environment.TickCount64 / 1000 / 60,
                localTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                dotnetVersion = Environment.Version.ToString()
            };

            return Task.FromResult(DaemonResponse.SuccessResponse(correlationId, data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DaemonResponse.ErrorResponse(correlationId, ex.Message));
        }
    }
}
