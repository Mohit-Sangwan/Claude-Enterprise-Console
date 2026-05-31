using ClaudeEnterprise.Application.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ClaudeEnterprise.Api.HealthChecks;

/// <summary>
/// Verifies the Anthropic credentials are configured. Used as a readiness
/// gate so orchestrators don't route traffic before the API key is present.
/// We deliberately do NOT make a real upstream call here — readiness must
/// stay fast and free.
/// </summary>
public sealed class AnthropicConfigHealthCheck : IHealthCheck
{
    private readonly AnthropicOptions _options;
    public AnthropicConfigHealthCheck(IOptions<AnthropicOptions> options) => _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var hasKey = !string.IsNullOrWhiteSpace(_options.ApiKey)
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

        return Task.FromResult(hasKey
            ? HealthCheckResult.Healthy("Anthropic API key configured.")
            : HealthCheckResult.Unhealthy("Anthropic API key not configured."));
    }
}
