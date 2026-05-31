using System.Threading.RateLimiting;
using Asp.Versioning;
using ClaudeEnterprise.Api.HealthChecks;
using ClaudeEnterprise.Api.Middleware;
using ClaudeEnterprise.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ClaudeEnterprise.Application.Chat;
using ClaudeEnterprise.Infrastructure;
using ClaudeEnterprise.Infrastructure.Persistence;
using FluentValidation;
using FluentValidation.AspNetCore;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ──────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ClaudeEnterprise.Api")
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

// ── Configuration & DI ───────────────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddOptions<SecurityOptions>()
    .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName));

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// ── API versioning ───────────────────────────────────────────────────────────
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true;
    o.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version"),
        new QueryStringApiVersionReader("api-version"));
}).AddMvc().AddApiExplorer(o =>
{
    o.GroupNameFormat = "'v'VVV";
    o.SubstituteApiVersionInUrl = true;
});

// ── Request size limits ──────────────────────────────────────────────────────
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 1_048_576; // 1 MiB
    o.ValueLengthLimit = 1_048_576;
});
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 4 * 1_048_576; // 4 MiB
    o.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
    o.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(120);
    o.AddServerHeader = false;
});

// ── HSTS (preload + 1 year) ──────────────────────────────────────────────────
builder.Services.AddHsts(o =>
{
    o.Preload = true;
    o.IncludeSubDomains = true;
    o.MaxAge = TimeSpan.FromDays(365);
});

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<SendMessageRequestValidator>();

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Claude Enterprise API",
        Version = "v1",
        Description = "Enterprise wrapper around Anthropic Claude — auth, rate-limiting, usage tracking, SSE streaming.",
    });
    c.AddSecurityDefinition(ApiKeyAuthenticationOptions.Scheme, new OpenApiSecurityScheme
    {
        Name = "X-API-Key",
        Description = "Required when Security:ApiKeys is configured.",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = ApiKeyAuthenticationOptions.Scheme,
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "OIDC/JWT bearer token. Enabled when Security:Jwt:Authority is configured.",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ApiKeyAuthenticationOptions.Scheme } },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        },
    });
});

// ── Authentication / Authorization ───────────────────────────────────────────
const string SmartScheme = "Smart";
var securitySection = builder.Configuration.GetSection(SecurityOptions.SectionName);
var jwtAuthority = securitySection.GetValue<string>("Jwt:Authority");
var jwtAudience = securitySection.GetValue<string>("Jwt:Audience");
var jwtRequireHttps = securitySection.GetValue<bool?>("Jwt:RequireHttpsMetadata") ?? !builder.Environment.IsDevelopment();

var authBuilder = builder.Services
    .AddAuthentication(SmartScheme)
    .AddPolicyScheme(SmartScheme, SmartScheme, o =>
    {
        o.ForwardDefaultSelector = ctx =>
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            return !string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : ApiKeyAuthenticationOptions.Scheme;
        };
    })
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationOptions.Scheme, _ => { });

if (!string.IsNullOrWhiteSpace(jwtAuthority))
{
    authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
    {
        o.Authority = jwtAuthority;
        o.Audience = jwtAudience;
        o.RequireHttpsMetadata = jwtRequireHttps;
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = "sub",
            RoleClaimType = "roles",
        };
    });
}
else
{
    // Stub bearer handler so [Authorize] with Bearer scheme doesn't crash when JWT is not configured.
    authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
    {
        o.RequireHttpsMetadata = false;
        o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = false, SignatureValidator = (t, _) => new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(t) };
    });
}

builder.Services.AddAuthorization(AuthorizationPolicies.Register);

// ── Health checks (liveness + readiness) ─────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck<AnthropicConfigHealthCheck>("anthropic-config", tags: new[] { "ready" });

// ── CORS ─────────────────────────────────────────────────────────────────────
const string CorsPolicy = "Frontend";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

// ── Rate limiting (per-IP token bucket) ──────────────────────────────────────
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 60,
                TokensPerPeriod = 30,
                ReplenishmentPeriod = TimeSpan.FromSeconds(60),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

// ── OpenTelemetry (tracing + metrics, OTLP exporter optional) ────────────────
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"]
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
var serviceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "ClaudeEnterprise.Api";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName, serviceVersion: "1.0.0"))
    .WithTracing(t =>
    {
        t.AddSource("ClaudeEnterprise.Anthropic")
         .AddAspNetCoreInstrumentation(o => o.RecordException = true)
         .AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation()
         .AddHttpClientInstrumentation()
         .AddRuntimeInstrumentation()
         .AddPrometheusExporter();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    });

var app = builder.Build();

// ── Database migration (auto on startup) ─────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetService<ChatDbContext>();
    if (db is not null)
    {
        await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }
}

// ── Pipeline ─────────────────────────────────────────────────────────────────
app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging(o =>
    o.EnrichDiagnosticContext = (diag, http) =>
    {
        diag.Set("CorrelationId", http.TraceIdentifier);
        diag.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString());
    });
app.UseMiddleware<AuditLogMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();


// Prometheus scrape endpoint
app.MapPrometheusScrapingEndpoint("/metrics");
app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

app.Run();

public partial class Program; // for tests
