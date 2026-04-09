using Orion.Daemon;
using Orion.Daemon.Core.Configuration;
using Orion.Daemon.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "OrionDaemon";
});

// Configuration
builder.Services.Configure<DaemonOptions>(
    builder.Configuration.GetSection("Daemon"));
builder.Services.Configure<ProactiveOptions>(
    builder.Configuration.GetSection("Proactive"));

// Register as singleton for injection
builder.Services.AddSingleton(sp => 
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProactiveOptions>>().Value);
builder.Services.AddSingleton(sp => 
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DaemonOptions>>().Value);

builder.Services.AddDaemonActions(builder.Configuration);
builder.Services.AddHostedService<DaemonWorker>();

var host = builder.Build();
host.Run();
