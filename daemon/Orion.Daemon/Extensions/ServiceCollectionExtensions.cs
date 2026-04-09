using Orion.Daemon.Actions;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Core.Interfaces;
using Orion.Daemon.Notifiers;

namespace Orion.Daemon.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDaemonActions(this IServiceCollection services, IConfiguration configuration)
    {
        // Configuration
        services.Configure<DaemonOptions>(configuration.GetSection("Daemon"));
        services.AddSingleton(sp =>
        {
            var options = new DaemonOptions();
            configuration.GetSection("Daemon").Bind(options);
            return options;
        });

        // Action registry
        services.AddSingleton<IActionRegistry, ActionRegistry>();

        // Register all actions
        services.AddSingleton<IAction>(sp =>
        {
            var options = sp.GetRequiredService<DaemonOptions>();
            return new OpenAppAction(options);
        });
        services.AddSingleton<IAction>(sp =>
        {
            var options = sp.GetRequiredService<DaemonOptions>();
            return new OpenFileInEditorAction(options);
        });
        services.AddSingleton<IAction>(sp =>
        {
            var options = sp.GetRequiredService<DaemonOptions>();
            return new RunScriptAction(options);
        });
        services.AddSingleton<IAction, OpenBrowserUrlAction>();
        services.AddSingleton<IAction, LaunchClaudeAction>();
        services.AddSingleton<IAction, GetSystemStatusAction>();
        services.AddSingleton<IAction>(sp =>
        {
            var options = sp.GetRequiredService<DaemonOptions>();
            return new ReadFileAction(options);
        });
        services.AddSingleton<IAction>(sp =>
        {
            var options = sp.GetRequiredService<DaemonOptions>();
            return new WriteFileAction(options);
        });
        services.AddSingleton<IAction>(sp =>
        {
            var options = sp.GetRequiredService<DaemonOptions>();
            return new GitStatusAction(options);
        });
        services.AddSingleton<IAction>(sp =>
        {
            var options = sp.GetRequiredService<DaemonOptions>();
            return new GitCommitAction(options);
        });

        // TTS Speakers
        services.AddSingleton<KokoroSpeaker>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<KokoroSpeaker>>();
            return new KokoroSpeaker(logger);
        });
        services.AddSingleton<PowerShellTtsNotifier>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PowerShellTtsNotifier>>();
            return new PowerShellTtsNotifier(logger);
        });

        // SpeakAction — lecture locale pour notifs proactives (Kokoro + fallback SAPI)
        services.AddSingleton<IAction>(sp =>
        {
            var speaker = sp.GetRequiredService<KokoroSpeaker>();
            var fallback = sp.GetRequiredService<PowerShellTtsNotifier>();
            var logger = sp.GetRequiredService<ILogger<SpeakAction>>();
            return new SpeakAction(speaker, fallback, logger);
        });

        // SynthesizeAction — retourne WAV bytes au backend → frontend AudioContext
        services.AddSingleton<IAction>(sp =>
        {
            var speaker = sp.GetRequiredService<KokoroSpeaker>();
            var logger = sp.GetRequiredService<ILogger<SynthesizeAction>>();
            return new SynthesizeAction(speaker, logger);
        });

        return services;
    }
}
