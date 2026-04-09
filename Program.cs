using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ShadowCat.Business.Services;
using ShadowCat.Core.Interfaces.Repositories;
using ShadowCat.Core.Interfaces.Services;
using ShadowCat.Core.Mappings;
using ShadowCat.Data.Context;
using ShadowCat.Data.Repositories;
using ShadowCat.Data.UnitOfWork;
using System.Text;
using System.Threading.RateLimiting;
using System.Security.Claims;
 
var builder = WebApplication.CreateBuilder(args);
 
// ========== CONTROLLERS ==========
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
 
// ========== SWAGGER AVEC JWT ==========
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ShadowCat API",
        Version = "v1",
        Description = "API sécurisée pour messagerie E2E chiffrée"
    });
    
    // ✅ Bouton Authorize dans Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
 
// ========== DATABASE ==========
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        b => b.MigrationsAssembly("ShadowCat.API")));
 
// ========== AUTHENTIFICATION JWT ==========
var jwtKey = builder.Configuration["Jwt:Key"] 
    ?? throw new InvalidOperationException("JWT Key not configured!");
 
if (jwtKey.Length < 32)
    throw new InvalidOperationException("JWT Key must be >= 32 chars");
 
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
    
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<Program>>();
            logger.LogWarning("JWT Auth failed: {Error}", 
                context.Exception.Message);
            return Task.CompletedTask;
        }
    };
});
 
builder.Services.AddAuthorization();
 
// ========== RATE LIMITING (.NET 7+) ==========
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    // Limite globale
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() 
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
    
    // Limite spécifique pour login
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() 
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
    
    // Limite pour register
    /*options.AddPolicy("register", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() 
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1)
            }));*/
});
 
// ========== REPOSITORIES ==========
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserCryptographicKeysRepository, 
    UserCryptographicKeysRepository>();  
builder.Services.AddScoped<IPreKeyBundleRepository, PreKeyBundleRepository>();
builder.Services.AddScoped<IOneTimePreKeyRepository, OneTimePreKeyRepository>();
 
// ========== UNIT OF WORK ==========
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
 
// ========== SERVICES ==========
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddScoped<IPreKeyBundleService, PreKeyBundleService>();
builder.Services.AddScoped<IOneTimePreKeyService, OneTimePreKeyService>();
builder.Services.AddScoped<ICryptoService, CryptoService>();
 
// ========== CURRENT USER SERVICE (pour audit) ==========
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddHttpContextAccessor();
 
// ========== AUTOMAPPER ==========
builder.Services.AddAutoMapper(
    typeof(Program).Assembly,
    typeof(UserMapping).Assembly);
 
// ========== CORS ==========
builder.Services.AddCors(options =>
{
    options.AddPolicy("ProductionPolicy", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",
                "http://localhost:3001", 
                "http://localhost:5173"
               )
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type")
            .AllowCredentials();
    });
    
    options.AddPolicy("DevelopmentPolicy", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000","http://localhost:3001", "http://localhost:5173")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});
 
// ========== HEALTH CHECKS ==========
builder.Services.AddHealthChecks();

var app = builder.Build();
 
// ========== MIDDLEWARE PIPELINE ==========
 
// Gestion globale des exceptions
app.UseExceptionHandler("/error");
 
// HTTPS
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
 
// Rate limiting
app.UseRateLimiter();
 
// Swagger (dev seulement)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("DevelopmentPolicy");
}
else
{
    app.UseCors("ProductionPolicy");
}
 
app.UseAuthentication();
app.UseAuthorization();
 
app.MapControllers();
app.MapHealthChecks("/health");
 
// Endpoint pour les erreurs
app.Map("/error", (HttpContext context) => Results.Problem());
 
app.Run();
