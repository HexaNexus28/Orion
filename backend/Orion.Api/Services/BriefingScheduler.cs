using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orion.Api.Controllers;
using Orion.Core.Interfaces.Agents;

namespace Orion.Api.Services;

/// <summary>
/// Déclenche automatiquement le briefing matinal à 8h chaque jour.
/// Broadcast via SSE → frontend lit à voix haute via TTS Kokoro/Web Speech.
/// </summary>
public class BriefingScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BriefingScheduler> _logger;

    private static readonly TimeOnly BriefingTime = new(8, 0);

    private readonly SseClientRegistry _sse;

    public BriefingScheduler(IServiceScopeFactory scopeFactory, ILogger<BriefingScheduler> logger, SseClientRegistry sse)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _sse = sse;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[BriefingScheduler] Started — triggers daily at {Time}", BriefingTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNext(BriefingTime);
            _logger.LogInformation("[BriefingScheduler] Next briefing in {Delay}", delay);

            await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            await TriggerBriefingAsync(stoppingToken);
        }
    }

    private async Task TriggerBriefingAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var agent = scope.ServiceProvider.GetRequiredService<IBriefingAgent>();

            _logger.LogInformation("[BriefingScheduler] Generating morning briefing...");
            var result = await agent.GenerateBriefingAsync(ct);

            if (!result.Success || result.Data == null)
            {
                _logger.LogWarning("[BriefingScheduler] Briefing generation failed: {Msg}", result.Message);
                return;
            }

            await ProactiveNotificationController.BroadcastAsync(
                eventType: "briefing",
                message: result.Data.Content,
                priority: "high",
                speak: true,
                _logger, _sse);

            _logger.LogInformation("[BriefingScheduler] Morning briefing broadcasted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BriefingScheduler] Failed to trigger briefing");
        }
    }

    private static TimeSpan ComputeDelayUntilNext(TimeOnly target)
    {
        var now = DateTime.Now;
        var todayTarget = now.Date.Add(target.ToTimeSpan());

        // If 8h already passed today, schedule for tomorrow
        if (now >= todayTarget)
            todayTarget = todayTarget.AddDays(1);

        return todayTarget - now;
    }
}
