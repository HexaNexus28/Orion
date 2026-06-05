using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Orion.Api.Middleware;
using Orion.Api.Services;
using Orion.Api.WebSockets;
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
            Email = "contact@example.com"
        }
    });
});

// ========== CONFIGURATION OPTIONS ==========
builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection(OllamaOptions.SectionName));
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
              .EnableRetryOnFailure(10, TimeSpan.FromSeconds(8), null)));

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
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<WebBrowseTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<ScreenshotTool>());

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
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<MemoryReflectTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<ProfileUpdateTool>());

logger.LogInformation(" Memory tools registered (memory_save, memory_update, memory_forget, memory_reflect, profile_update)");

// ========== SYSTEM TOOLS (Daemon) ==========
builder.Services.AddScoped<GetSystemStatusTool>();
builder.Services.AddScoped<GitStatusTool>();
builder.Services.AddScoped<OpenAppTool>();
builder.Services.AddScoped<OpenBrowserUrlTool>();
builder.Services.AddScoped<ReadFileTool>();
builder.Services.AddScoped<GitCommitTool>();
builder.Services.AddScoped<WriteFileTool>();
builder.Services.AddScoped<RunScriptTool>();
builder.Services.AddScoped<ListFilesTool>();
builder.Services.AddScoped<KillProcessTool>();
builder.Services.AddScoped<ClipboardTool>();
builder.Services.AddScoped<TypeTextTool>();
builder.Services.AddScoped<CaptureScreenTool>();

// Register system tools as ITool for ToolRegistry auto-discovery
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<GetSystemStatusTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<GitStatusTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<OpenAppTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<OpenBrowserUrlTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<ReadFileTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<GitCommitTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<WriteFileTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<RunScriptTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<ListFilesTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<KillProcessTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<ClipboardTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<TypeTextTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<CaptureScreenTool>());

logger.LogInformation(" System tools registered (13 tools: status, git, app, browser, files, clipboard, keyboard, screen)");

// ========== DAEMON ==========
builder.Services.AddSingleton<IDaemonClient, DaemonWebSocketClient>();
builder.Services.AddSingleton<DaemonActionValidator>();

logger.LogInformation(" Daemon client registered");

// ========== AGENTS (Business Layer Internals) ==========
builder.Services.AddScoped<IConversationAgent, ConversationAgent>();
builder.Services.AddScoped<IBriefingAgent, BriefingAgent>();

// ========== BUSINESS SERVICES (API Interface) ==========
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<ILLMService, LLMService>();
builder.Services.AddScoped<IMemoryService, MemoryService>();
builder.Services.AddScoped<IToolService, ToolService>();
builder.Services.AddScoped<IBriefingService, BriefingService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IHealthService, HealthService>();

// ========== EMBEDDING SERVICE (RAG - nomic-embed-text) ==========
builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
logger.LogInformation(" Embedding Service registered (Ollama nomic-embed-text)");

// ========== VOICE SERVICE (Phase 4 - Whisper STT) ==========
builder.Services.AddSingleton<IWhisperService, WhisperService>();
builder.Services.AddScoped<IVoiceNotificationService, VoiceNotificationService>();
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
            .AllowAnyHeader()
            .WithExposedHeaders("X-Transcript");
    });
    
    options.AddPolicy("ProductionPolicy", policy =>
    {
        policy
            .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() 
                ?? Array.Empty<string>())
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type")
            .WithExposedHeaders("X-Transcript")
            .AllowCredentials();
    });
});

// ========== HEALTH CHECKS ==========
builder.Services.AddHealthChecks();

// ========== BACKGROUND SERVICES ==========
builder.Services.AddHostedService<BriefingScheduler>();

// ========== BUILD APP ==========
var app = builder.Build();

// ========== MIDDLEWARE PIPELINE ==========

// Error handling
app.UseMiddleware<ErrorHandlingMiddleware>();

// WebSocket support for daemon + voice
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
    AllowedOrigins = { "http://localhost:5173", "http://localhost:3000" }
});
app.UseMiddleware<DaemonWebSocketMiddleware>();
app.UseMiddleware<VoiceWebSocketMiddleware>();

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
