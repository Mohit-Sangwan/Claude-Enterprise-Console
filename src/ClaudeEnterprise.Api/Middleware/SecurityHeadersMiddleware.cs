namespace ClaudeEnterprise.Api.Middleware;

/// <summary>
/// Applies a conservative set of security response headers suitable for an
/// internal API + first-party SPA. CSP is intentionally permissive enough
/// to allow Swagger UI and inline styles bundled by the static frontend.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext ctx)
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            h["Cross-Origin-Resource-Policy"] = "same-origin";
            h["X-Permitted-Cross-Domain-Policies"] = "none";
            // CSP — first-party SPA + Swagger UI need 'unsafe-inline' for style;
            // scripts are bundled assets only.
            h["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; " +
                "connect-src 'self'; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'";
            return Task.CompletedTask;
        });
        return _next(ctx);
    }
}
