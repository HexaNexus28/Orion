using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Orion.Api.Middleware;
using Orion.Business.Agents;
using Orion.Business.Daemon;
using Orion.Business.LLM;
using Orion.Business.Services;
using Orion.Business.Tools;
using Orion.Business.Tools.Internet;
using Orion.Business.Tools.Memory;
using Orion.Business.Tools.System;
using Orion.Core.Configuration;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;
using Orion.Core.Interfaces.Tools;
using Orion.Data.Context;
using Orion.Data.UnitOfWork;

var builder = WebApplication.CreateBuilder(args);

// ========== CONFIGURATION & LOGGING ==========
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Logger l'environnement au démarrage
var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger<Program>();
logger.LogInformation(" ORION API starting - Environment: {Environment}", 
    builder.Environment.EnvironmentName);

// ========== SWAGGER ==========
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ORION API",
        Version = "v1",
        Description = "API pour l'assistant IA personnel ORION",
        Contact = new OpenApiContact
        {
            Name = "Yawo Zoglo",
            Email = "contact@shift-star.app"
        }
    });
});

// ========== CONFIGURATION OPTIONS ==========
builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.Configure<AnthropicOptions>(
    builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.Configure<SupabaseOptions>(
    builder.Configuration.GetSection(SupabaseOptions.SectionName));
builder.Services.Configure<DaemonOptions>(
    builder.Configuration.GetSection(DaemonOptions.SectionName));
builder.Services.Configure<InternetOptions>(
    builder.Configuration.GetSection(InternetOptions.SectionName));

// ========== DATABASE ==========
var supabaseConnection = builder.Configuration.GetConnectionString("Supabase") 
    ?? builder.Configuration.GetSection("Supabase:ConnectionString").Value;

if (string.IsNullOrEmpty(supabaseConnection))
{
    logger.LogError(" Supabase connection string not configured! Please set ConnectionStrings:Supabase in appsettings.Development.json");
    throw new InvalidOperationException("Supabase connection string is required. See appsettings.Development.json template.");
}

builder.Services.AddDbContext<OrionDbContext>(options =>
    options.UseNpgsql(supabaseConnection, npgsql =>
        npgsql.MigrationsAssembly("Orion.Data")
              .EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));

logger.LogInformation(" Database configured (PostgreSQL)");

// ========== REPOSITORIES & UNIT OF WORK ==========
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IToolRegistry, ToolRegistry>();
logger.LogInformation(" Repositories & UnitOfWork registered");

// ========== LLM CLIENTS ==========
builder.Services.AddHttpClient<ILLMClient, OllamaClient>("Ollama", client =>
{
    client.Timeout = TimeSpan.FromMinutes(3); // 3 min timeout for model loading
});

logger.LogInformation(" LLM Client registered (Ollama HTTP mode)");

// ========== LLM ROUTER ==========
builder.Services.AddSingleton<ILLMRouter, LLMRouter>();
builder.Services.AddSingleton<PromptBuilder>();

// ========== INTERNET TOOLS (Phase 3) ==========
builder.Services.AddHttpClient<WebSearchTool>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<WebFetchTool>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; ORION/1.0)");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<WebSearchTool>();
builder.Services.AddScoped<WebFetchTool>();
builder.Services.AddScoped<WebBrowseTool>();
builder.Services.AddScoped<ScreenshotTool>();

// Register internet tools as ITool for ToolRegistry auto-discovery
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<WebSearchTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<WebFetchTool>());

logger.LogInformation(" Internet tools registered (WebSearch, WebFetch, WebBrowse, Screenshot)");

// ========== MEMORY TOOLS (ORION Autonomous) ==========
builder.Services.AddScoped<MemorySaveTool>();
builder.Services.AddScoped<MemoryUpdateTool>();
builder.Services.AddScoped<MemoryForgetTool>();
builder.Services.AddScoped<MemoryReflectTool>();
builder.Services.AddScoped<ProfileUpdateTool>();

// Register memory tools as ITool for ToolRegistry auto-discovery
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<MemorySaveTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<MemoryUpdateTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<MemoryForgetTool>());

logger.LogInformation(" Memory tools registered (memory_save, memory_update, memory_forget, memory_reflect, profile_update)");

// ========== SYSTEM TOOLS (Daemon) ==========
builder.Services.AddScoped<GetSystemStatusTool>();
builder.Services.AddScoped<GitStatusTool>();
builder.Services.AddScoped<OpenAppTool>();
builder.Services.AddScoped<OpenBrowserUrlTool>();
builder.Services.AddScoped<ReadFileTool>();
builder.Services.AddScoped<GitCommitTool>();

// Register system tools as ITool for ToolRegistry auto-discovery
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<GetSystemStatusTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<GitStatusTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<OpenAppTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<OpenBrowserUrlTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<ReadFileTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<GitCommitTool>());

logger.LogInformation(" System tools registered (get_system_status, git_status, open_app, open_browser_url, read_file, git_commit)");

// ========== DAEMON ==========
builder.Services.AddSingleton<IDaemonClient, DaemonWebSocketClient>();
builder.Services.AddSingleton<DaemonActionValidator>();

logger.LogInformation(" Daemon client registered");

// ========== AGENTS (Business Layer Internals) ==========
builder.Services.AddScoped<IConversationAgent, ConversationAgent>();

// ========== BUSINESS SERVICES (API Interface) ==========
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<ILLMService, LLMService>();
builder.Services.AddScoped<IMemoryService, MemoryService>();
builder.Services.AddScoped<IToolService, ToolService>();
builder.Services.AddScoped<IBriefingService, BriefingService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IHealthService, HealthService>();

// ========== VOICE SERVICE (Phase 4 - Whisper STT) ==========
builder.Services.AddSingleton<IWhisperService, WhisperService>();
builder.Services.AddScoped<VoiceNotificationService>();
logger.LogInformation(" Voice Service registered (Whisper STT + TTS notification)");

logger.LogInformation(" Business Services registered (including Audit)");

// ========== CORS ==========
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentPolicy", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "http://localhost:3000")
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
    
    options.AddPolicy("ProductionPolicy", policy =>
    {
        policy
            .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() 
                ?? Array.Empty<string>())
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type")
            .AllowCredentials();
    });
});

// ========== HEALTH CHECKS ==========
builder.Services.AddHealthChecks();

// ========== BUILD APP ==========
var app = builder.Build();

// ========== MIDDLEWARE PIPELINE ==========

// Error handling
app.UseMiddleware<ErrorHandlingMiddleware>();

// WebSocket support for daemon
app.UseWebSockets();
app.UseMiddleware<DaemonWebSocketMiddleware>();

// HTTPS
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ORION API v1");
        c.RoutePrefix = "swagger";
    });
    app.UseCors("DevelopmentPolicy");
    logger.LogInformation(" Swagger UI available at /swagger");
}
else
{
    app.UseCors("ProductionPolicy");
}

// Authorization disabled in development - no JWT auth configured yet
// app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

logger.LogInformation(" ORION API ready - Health check at /health");

app.Run();
