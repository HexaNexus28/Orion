using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
﻿using Microsoft.EntityFrameworkCore;
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
builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.Configure<NimOptions>(
    builder.Configuration.GetSection(NimOptions.SectionName));

// Embeddings : fournisseur compatible OpenAI, choisi par CONFIGURATION (mistral-embed 1024 dims
// par defaut, mesure vivant le 2026-08-25). Ollama a ete retire du chemin de production : il
// n existe pas sur le VPS, la memoire y serait morte en silence.
builder.Services.Configure<EmbeddingOptions>(
    builder.Configuration.GetSection(EmbeddingOptions.SectionName));

// ========== AUTHENTIFICATION ==========
// L'API etait TOTALEMENT ouverte : UseAuthorization commentee, aucun [Authorize]. Expose
// publiquement, n'importe qui pouvait lire la memoire et surtout METTRE DES ACTIONS EN FILE,
// executees ensuite sur la machine de l'utilisateur. Voir AuthOptions pour le raisonnement.
builder.Services.Configure<AuthOptions>(
    builder.Configuration.GetSection(AuthOptions.SectionName));

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "orion",
            ValidAudience = "orion",
            // Cle vide si non configuree : aucune signature ne peut etre validee, donc tout
            // est refuse. C'est le comportement voulu — fail-closed.
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(authOptions.IsConfigured ? authOptions.JwtSecret : Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")))
        };
    });

// Politique par DEFAUT : tout controleur exige une session, sauf [AllowAnonymous] explicite.
// L'inverse (proteger controleur par controleur) laisse passer tout ce qu'on oublie.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
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

// Point d'application UNIQUE de l'execution d'outil : c'est lui qui decide d'executer, de
// differer ou de refuser. Ni la boucle agent ni l'API outils n'appellent ExecuteAsync en direct.
builder.Services.AddScoped<IToolInvoker, ToolInvoker>();
logger.LogInformation(" Repositories & UnitOfWork registered");


// ========== BOUCLE AGENT (chantier 1 — Jarvis) ==========
// Transport dedie : streaming AVEC tools, ce que ILLMClient ne peut structurellement pas porter.
var ollamaBaseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
var ollamaTimeout = builder.Configuration.GetValue<int?>("Ollama:TimeoutSeconds") ?? 120;
builder.Services.AddHttpClient(OllamaAgentClient.HttpClientName, client =>
{
    client.BaseAddress = new Uri(ollamaBaseUrl);
    // Valeur de config, pas une constante en dur : sur un modele local en CPU, la PREMIERE
    // requete paie le chargement du modele ET l'evaluation a froid du prompt systeme
    // (242 s mesurees le 2026-08-20). Les suivantes tombent a moins d'une seconde grace au
    // cache de prefixe. Un provider distant n'a evidemment pas ce probleme.
    client.Timeout = TimeSpan.FromSeconds(ollamaTimeout);
});
// NVIDIA NIM — cerveau distant, compatible OpenAI.
var nimBaseUrl = builder.Configuration["Nim:BaseUrl"] ?? "https://integrate.api.nvidia.com/v1";
var nimTimeout = builder.Configuration.GetValue<int?>("Nim:TimeoutSeconds") ?? 120;
builder.Services.AddHttpClient(NimAgentClient.HttpClientName, client =>
{
    // L'URL de base DOIT finir par '/' pour que les chemins relatifs se concatenent.
    client.BaseAddress = new Uri(nimBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(nimTimeout);
});

// Singletons : chaque client memorise le modele qui repond reellement, elu une fois par la sonde.
builder.Services.AddSingleton<NimAgentClient>();
builder.Services.AddSingleton<OllamaAgentClient>();

// L'ORDRE EST LA POLITIQUE : distant d'abord (qualite), local en dernier (hors-ligne degrade).
builder.Services.AddSingleton<ILLMAgentClient>(sp => new LLMCascade(
    new ILLMAgentClient[]
    {
        sp.GetRequiredService<NimAgentClient>(),
        sp.GetRequiredService<OllamaAgentClient>(),
    },
    sp.GetRequiredService<ILogger<LLMCascade>>()));

builder.Services.AddScoped<IAgentLoop, AgentLoop>();
logger.LogInformation(" Agent loop registered (cascade NIM -> Ollama, streaming + tools)");

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
builder.Services.AddScoped<ProactiveFeedbackTool>();

// Register memory tools as ITool for ToolRegistry auto-discovery
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<MemorySaveTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<MemoryUpdateTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<MemoryForgetTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<MemoryReflectTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<ProfileUpdateTool>());
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<ProactiveFeedbackTool>());

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

// File des actions demandees pendant que le PC etait eteint.
builder.Services.AddScoped<IDeferredActionService, DeferredActionService>();

logger.LogInformation(" Daemon client registered");

// ========== AGENTS (Business Layer Internals) ==========
builder.Services.AddScoped<IConversationAgent, ConversationAgent>();
builder.Services.AddScoped<IBriefingAgent, BriefingAgent>();

// ========== BUSINESS SERVICES (API Interface) ==========
builder.Services.AddScoped<ILLMService, LLMService>();
builder.Services.AddScoped<IProactiveLearningService, ProactiveLearningService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IMemoryService, MemoryService>();
builder.Services.AddScoped<IMemoryConsolidator, MemoryConsolidator>();
builder.Services.AddScoped<IMemoryRevectorizer, MemoryRevectorizer>();
builder.Services.AddScoped<IToolService, ToolService>();
builder.Services.AddScoped<IBriefingService, BriefingService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IHealthService, HealthService>();

// ========== EMBEDDING SERVICE (RAG — NVIDIA NIM) ==========
// Ollama a ete retire : il n existe pas sur le VPS, la memoire y serait morte en silence.
builder.Services.AddHttpClient<IEmbeddingService, OpenAiCompatibleEmbeddingService>();
logger.LogInformation(" Embedding Service registered (fournisseur compatible OpenAI — Ollama retire du chemin de production)");

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

// Draine la file des le retour du daemon, et expire ce qui a trop attendu.
builder.Services.AddHostedService<DeferredActionWatcher>();

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

// Ordre IMPERATIF : authentification (qui es-tu) avant autorisation (as-tu le droit).
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
// /health reste ouvert : sonde du conteneur Docker et de la facade Nginx. Il n expose
// aucune donnee, seulement l etat du service.
app.MapHealthChecks("/health").AllowAnonymous();

// ========== SONDE LLM AU DEMARRAGE ==========
// On APPELLE le modele au lieu de faire confiance a la config : un modele liste par
// `ollama list` peut etre retire ou verrouille par abonnement. Sans cette sonde, la panne
// est invisible et ORION bascule en silence sur un modele degrade
// (docs/jarvis-gap-analysis.md §1.10 / §1.11).
using (var probeScope = app.Services.CreateScope())
{
    var llmClient = probeScope.ServiceProvider.GetRequiredService<ILLMAgentClient>();
    using var probeCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    if (await llmClient.ProbeAsync(probeCts.Token))
    {
        logger.LogInformation(" LLM operationnel — {Provider} / {Model}",
            llmClient.Provider, llmClient.ModelId);
    }
    else
    {
        logger.LogCritical(
            " AUCUN FOURNISSEUR LLM ACCESSIBLE. Verifie la cle NIM (section 'Nim:ApiKey') et "
            + "qu'Ollama tourne ({BaseUrl}). ORION demarre mais ne pourra ni repondre ni agir.",
            ollamaBaseUrl);
    }
}

logger.LogInformation(" ORION API ready - Health check at /health");

app.Run();
