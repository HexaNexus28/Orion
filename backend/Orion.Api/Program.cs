using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.IdentityModel.Tokens;
﻿using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Orion.Api.Authentication;
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
        Description = "API pour l'assistant IA personnel ORION"
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
// Le secret du daemon est lu ICI, une seule fois, et range dans AuthOptions comme le reste.
// Avant, un `Environment.GetEnvironmentVariable` trainait au milieu d'un middleware : la
// configuration d'authentification vivait a deux endroits sans rapport l'un avec l'autre.
// ASP.NET journalise l URL COMPLETE de chaque requete (« Request starting GET /ws/voice?... »).
// Le billet de flux s y retrouve donc EN CLAIR dans les journaux du conteneur — constate le
// 2026-08-26, apres avoir cru la fuite fermee parce que nginx, lui, la masquait.
//
// Ces lignes sont une DUPLICATION : nginx journalise deja chaque requete, avec le jeton
// remplace par ***. On coupe donc la copie non masquee plutot que d ecrire un filtre de plus.
// Warning et non None : une exception non geree du pipeline doit rester visible.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
}

builder.Services.PostConfigure<AuthOptions>(o =>
    o.DaemonToken = Environment.GetEnvironmentVariable("DAEMON_WS_TOKEN") ?? o.DaemonToken);

// UNE porte d'entree. Le schema par defaut est un SELECTEUR, pas un validateur : il regarde la
// requete et delegue au bon schema. Sans lui, `UseAuthentication()` n'executerait que JWT et le
// daemon n'aurait jamais d'identite, quel que soit le nombre de schemas declares.
builder.Services.AddAuthentication(OrionAuth.SelectorScheme)
    .AddPolicyScheme(OrionAuth.SelectorScheme, OrionAuth.SelectorScheme, options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey(OrionAuth.DaemonTokenHeader)
                ? OrionAuth.DaemonScheme
                : JwtBearerDefaults.AuthenticationScheme;
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DaemonAuthenticationHandler>(
        OrionAuth.DaemonScheme, null)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = OrionAuth.Issuer,
            // DEUX audiences acceptees ici, mais PAS interchangeables : OnTokenValidated verifie
            // ensuite que chacune est utilisee la ou elle doit l etre. Sans cette seconde etape,
            // declarer les deux audiences reviendrait a les rendre equivalentes.
            ValidAudiences = new[] { OrionAuth.Audience, OrionAuth.StreamAudience },
            // Explicite : c.est ce claim que lisent `RequireRole` et le garde WebSocket. Le laisser
            // implicite marcherait par defaut, mais un echec de mappage se traduirait par un 403
            // partout, sans message — le genre de panne qu.on cherche pendant une heure.
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            // Cle vide si non configuree : aucune signature ne peut etre validee, donc tout
            // est refuse. C'est le comportement voulu — fail-closed.
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(authOptions.IsConfigured ? authOptions.JwtSecret : Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")))
        };

        // EventSource et WebSocket cote navigateur ne peuvent porter AUCUN en-tete — limite des
        // navigateurs, pas choix d'implementation. Le jeton passe donc par l'URL, et UNIQUEMENT
        // sur les chemins listes dans OrionAuth.QueryTokenPaths. La liste est fermee et vit a un
        // seul endroit : c'est ce qui empeche ce contournement de s'etendre par negligence.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    OrionAuth.AllowsQueryToken(context.HttpContext.Request.Path))
                {
                    context.Token = accessToken;
                    // Marque indispensable a OnTokenValidated : sans elle, impossible de
                    // distinguer un jeton lu dans l URL d un jeton lu dans l en-tete.
                    context.HttpContext.Items[OrionAuth.TokenVenuDeLUrl] = true;
                }
                return Task.CompletedTask;
            },

            // Le jeton de session ne doit JAMAIS voyager dans une URL : une URL finit dans les
            // journaux du serveur, ceux du CDN, l historique du navigateur et l en-tete Referer.
            // Constate en production le 2026-08-26 — un jeton valide 30 jours en clair dans
            // access.log. Seul un BILLET DE FLUX, valable une minute, a le droit d y figurer.
            //
            // Le controle vaut dans LES DEUX SENS, et c est ce qui le rend efficace :
            //   - jeton de session dans l URL  -> refuse (force l usage du billet)
            //   - billet hors d un chemin de flux -> refuse (un billet vole ne donne acces a rien)
            OnTokenValidated = context =>
            {
                // Depuis .NET 8, JwtBearer valide avec JsonWebTokenHandler : SecurityToken est
                // un JsonWebToken, PAS un JwtSecurityToken. Un simple `as JwtSecurityToken`
                // renvoyait null, donc une liste d audiences VIDE, donc estBillet toujours faux
                // — le controle etait inerte dans un sens et un billet passait comme jeton de
                // session. On accepte les deux types plutot que de parier sur le handler actif.
                var audiences = context.SecurityToken switch
                {
                    JsonWebToken jwt        => jwt.Audiences,
                    JwtSecurityToken ancien => ancien.Audiences,
                    _                       => Enumerable.Empty<string>()
                };
                var estBillet = audiences.Contains(OrionAuth.StreamAudience);
                var venuDeLUrl = context.HttpContext.Items.ContainsKey(OrionAuth.TokenVenuDeLUrl);

                if (estBillet && !venuDeLUrl)
                {
                    context.Fail("Un billet de flux ne peut pas servir de jeton de session.");
                }
                else if (!estBillet && venuDeLUrl)
                {
                    context.Fail("Le jeton de session ne peut pas voyager dans une URL — utiliser un billet de flux.");
                }

                return Task.CompletedTask;
            }
        };
    });

// FERME PAR DEFAUT, en un seul endroit. Toute route sans attribut exige le proprietaire : une
// route oubliee est refusee, jamais ouverte. Le daemon ne satisfait PAS cette politique — son
// secret n'ouvre que les deux routes qui la declarent explicitement.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(OrionAuth.OwnerPolicy, p => p.RequireRole(OrionAuth.OwnerRole));
    options.AddPolicy(OrionAuth.DaemonPolicy, p => p.RequireRole(OrionAuth.DaemonRole));

    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireRole(OrionAuth.OwnerRole)
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

// Ce sur quoi l utilisateur travaille MAINTENANT — fichier et projet ouverts. Premier widget
// PERMANENT du HUD : il reste a l ecran et se rafraichit, au lieu d apparaitre puis disparaitre.
builder.Services.AddScoped<GetWorkContextTool>();
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
builder.Services.AddScoped<ITool>(sp => sp.GetRequiredService<GetWorkContextTool>());
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
// ── Transcription : distant en tete, local en repli ────────────────────────────────────────
//
// Whisper small en local mettait 5,0 s pour 4,6 s de parole, meme apres avoir quadruple les
// ressources du conteneur. Voxtral (Mistral) fait 0,35 s et transcrit MIEUX — mesure du
// 2026-08-27 sur le meme audio. La cle est celle qui sert deja aux embeddings : aucun compte
// de plus, quotas hors d atteinte (3600 s d audio par minute).
//
// Le local reste enregistre DERRIERE : si Mistral tombe ou change ses conditions, la voix
// continue de fonctionner en degrade au lieu de s arreter. Meme motif que LLMCascade.
builder.Services.Configure<TranscriptionOptions>(
    builder.Configuration.GetSection(TranscriptionOptions.SectionName));

// La cle retombe sur celle des embeddings : meme fournisseur, meme compte. Dupliquer le secret
// dans le coffre creerait deux valeurs a faire tourner ensemble — une seule finirait par l etre.
builder.Services.PostConfigure<TranscriptionOptions>(o =>
{
    if (string.IsNullOrWhiteSpace(o.ApiKey))
        o.ApiKey = builder.Configuration["Embedding:ApiKey"] ?? string.Empty;
});

var transcriptionOptions = builder.Configuration.GetSection(TranscriptionOptions.SectionName)
    .Get<TranscriptionOptions>() ?? new TranscriptionOptions();

builder.Services.AddHttpClient(VoxtralTranscriptionService.HttpClientName, client =>
{
    client.BaseAddress = new Uri(transcriptionOptions.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(transcriptionOptions.TimeoutSeconds);
});

builder.Services.AddSingleton<WhisperService>();
builder.Services.AddSingleton<VoxtralTranscriptionService>();

// L ORDRE de ce tableau EST la politique de repli. Rien d autre ne la decide.
builder.Services.AddSingleton<IWhisperService>(sp => new TranscriptionCascade(
    new IWhisperService[]
    {
        sp.GetRequiredService<VoxtralTranscriptionService>(),
        sp.GetRequiredService<WhisperService>(),
    },
    sp.GetRequiredService<ILogger<TranscriptionCascade>>()));
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
// Singleton : la liste des clients SSE doit survivre aux requetes — un flux vit des heures.
// Extrait du controleur parce que les services d arriere-plan doivent diffuser eux aussi.
builder.Services.AddSingleton<SseClientRegistry>();

// Panneaux permanents du HUD (etape C) : rejoue un outil existant et diffuse SA carte.
builder.Services.AddHostedService<HudBroadcastService>();

builder.Services.AddHostedService<BriefingScheduler>();

// Draine la file des le retour du daemon, et expire ce qui a trop attendu.
builder.Services.AddHostedService<DeferredActionWatcher>();

// ========== BUILD APP ==========
var app = builder.Build();

// ========== MIDDLEWARE PIPELINE ==========

// Error handling
app.UseMiddleware<ErrorHandlingMiddleware>();


// ========== PWA SERVIE PAR LE BACKEND ==========
// Le bundle construit vit dans wwwroot (cf. Dockerfile). Le servir ICI, avant
// l'authentification, est VOLONTAIRE : la coquille de l'application n'est pas un secret, et
// si elle exigeait une session l'utilisateur n'aurait jamais l'ecran pour en ouvrir une.
// C'est l'API qui est protegee, pas le HTML qui permet de s'y connecter.
app.UseDefaultFiles();

// Types MIME EXPLICITES pour ce que la PWA embarque.
//
// UseStaticFiles ne sert QUE les extensions qu il connait. Une extension inconnue n est pas
// refusee : elle est IGNOREE. La requete continue vers le routage, ne trouve aucun point
// d entree, et la politique fermee par defaut repond 401 — un code qui envoie chercher du cote
// de l authentification alors que le probleme est une table de types MIME.
//
// Constate le 2026-08-27 : /vad/silero_vad_v5.onnx renvoyait 401 pendant que le worklet .js
// voisin renvoyait 200. Le moteur ONNX recevait un corps vide et signalait « aucun graphe dans
// le protobuf » — une erreur qui ne pointait vers rien de vrai. Le detecteur de parole ne
// pouvait donc PAS demarrer, et l interface parlait d un micro absent.
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".onnx"] = "application/octet-stream";   // modele Silero
contentTypes.Mappings[".bin"] = "application/octet-stream";    // poids de modele

app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });

// WebSocket — daemon + voix.
//
// AllowedOrigins etait EN DUR sur localhost. Quand cette liste est non vide, toute connexion
// dont l.en-tete Origin n.y figure pas est REJETEE. Depuis un telephone le navigateur envoie
// https://orion.shift-star.app : la voix etait donc refusee en silence en production, alors que
// le WebSocket du daemon passait — un client non-navigateur n.envoie aucun Origin. Meme source
// de verite que le CORS : deux listes d.origines finissent toujours par diverger.
// ATTENTION : AllowedOrigins ne peut PAS venir de appsettings.json, qui est gitignore donc
// absent de l image. En production la valeur arrive par la variable d environnement
// AllowedOrigins__0, posee par Ansible depuis le domaine declare de la stack.
var wsOptions = new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) };
foreach (var origin in builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
{
    wsOptions.AllowedOrigins.Add(origin);
}

// Les origines de developpement ne sont ajoutees QU en developpement. En production elles
// n autoriseraient rien d utile et elargiraient la surface pour rien.
if (app.Environment.IsDevelopment())
{
    wsOptions.AllowedOrigins.Add("http://localhost:5173");
    wsOptions.AllowedOrigins.Add("http://localhost:3000");
}

// Liste VIDE = le framework accepte TOUTE origine. En production ce serait la porte ouverte au
// detournement de WebSocket inter-sites : une page tierce ouvrirait un canal vers l assistant.
// On refuse de demarrer plutot que de tourner grand ouvert sans que personne ne le remarque.
if (wsOptions.AllowedOrigins.Count == 0)
{
    throw new InvalidOperationException(
        "Aucune origine WebSocket autorisee. Definir AllowedOrigins__0 (cf. env de la stack Ansible).");
}
app.UseWebSockets(wsOptions);
// L.authentification est remontee ICI, et pas laissee a sa place habituelle plus bas, parce
// que les middlewares WebSocket en ont besoin : sans elle `context.User` serait vide et aucun
// controle ne serait possible dans /ws/voice — c.est precisement pourquoi ce canal est reste
// ouvert a tous. `UseAuthentication` ne REJETTE rien par lui-meme (c.est `UseAuthorization` qui
// rejette) : la remonter n.a donc aucun effet sur les routes existantes.
//
// L.inverse — descendre les WebSocket sous l.authentification — casserait tout : ils
// passeraient alors par `UseHttpsRedirection`, or derriere nginx l.application voit du HTTP en
// loopback. L.upgrade partirait en redirection 307 et les DEUX WebSocket tomberaient.
app.UseAuthentication();

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

// Autorisation seule : l.authentification est deja passee, bien plus haut (cf. le bloc
// WebSocket). L.ordre authentification -> autorisation reste respecte.
app.UseAuthorization();

app.MapControllers();

// Routage cote client : une URL profonde ouverte directement (ou un rechargement) doit rendre
// index.html, pas un 404. AllowAnonymous est OBLIGATOIRE — la politique par defaut exige une
// session, et sans cette exception l'ecran de connexion lui-meme partirait en 401.
app.MapFallbackToFile("index.html").AllowAnonymous();
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
