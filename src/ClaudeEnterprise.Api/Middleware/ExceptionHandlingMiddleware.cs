using System.Net;
using System.Text.Json;
using Anthropic.Exceptions;
using FluentValidation;

namespace ClaudeEnterprise.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // client disconnected; swallow
        }
        catch (ValidationException ex)
        {
            await WriteProblem(ctx, HttpStatusCode.BadRequest, "Validation failed",
                string.Join("; ", ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))).ConfigureAwait(false);
        }
        catch (AnthropicRateLimitException ex)
        {
            _logger.LogWarning(ex, "Upstream rate limit");
            await WriteProblem(ctx, HttpStatusCode.TooManyRequests, "Upstream rate limit", ex.Message).ConfigureAwait(false);
        }
        catch (AnthropicUnauthorizedException ex)
        {
            _logger.LogError(ex, "Upstream auth failure");
            await WriteProblem(ctx, HttpStatusCode.BadGateway, "Upstream authentication failure", "Invalid API key.").ConfigureAwait(false);
        }
        catch (Anthropic5xxException ex)
        {
            _logger.LogError(ex, "Upstream 5xx");
            await WriteProblem(ctx, HttpStatusCode.BadGateway, "Upstream service error", ex.Message).ConfigureAwait(false);
        }
        catch (AnthropicException ex)
        {
            _logger.LogError(ex, "Anthropic SDK error");
            await WriteProblem(ctx, HttpStatusCode.BadGateway, "Upstream error", ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteProblem(ctx, HttpStatusCode.InternalServerError, "Internal Server Error",
                "An unexpected error occurred.").ConfigureAwait(false);
        }
    }

    private static async Task WriteProblem(HttpContext ctx, HttpStatusCode status, string title, string detail)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.Clear();
        ctx.Response.StatusCode = (int)status;
        ctx.Response.ContentType = "application/problem+json";
        var payload = new
        {
            type = $"https://httpstatuses.io/{(int)status}",
            title,
            status = (int)status,
            detail,
            traceId = ctx.TraceIdentifier,
            correlationId = ctx.TraceIdentifier,
            instance = ctx.Request.Path.ToString(),
        };
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(payload)).ConfigureAwait(false);
    }
}
