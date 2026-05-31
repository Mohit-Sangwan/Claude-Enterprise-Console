using System.Diagnostics;
using System.Security.Claims;

namespace ClaudeEnterprise.Api.Middleware;

/// <summary>
/// Structured audit log: one log event per non-trivial request with
/// correlation id, principal, route, status, duration, and bytes.
/// Stays out of the request-logging pipeline so it can be routed to
/// a separate sink in production (Splunk/ELK/etc.).
/// </summary>
public sealed class AuditLogMiddleware
{
    private static readonly HashSet<string> SkipPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health", "/health/live", "/health/ready", "/metrics", "/favicon.ico",
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLogMiddleware> _logger;

    public AuditLogMiddleware(RequestDelegate next, ILogger<AuditLogMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (SkipPaths.Contains(path) || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx).ConfigureAwait(false);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await _next(ctx).ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            var user = ctx.User?.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
            _logger.LogInformation(
                "AUDIT method={Method} path={Path} status={Status} user={User} ip={Ip} ms={Elapsed} corrId={CorrelationId}",
                ctx.Request.Method,
                path,
                ctx.Response.StatusCode,
                user,
                ctx.Connection.RemoteIpAddress?.ToString(),
                sw.ElapsedMilliseconds,
                ctx.TraceIdentifier);
        }
    }
}
