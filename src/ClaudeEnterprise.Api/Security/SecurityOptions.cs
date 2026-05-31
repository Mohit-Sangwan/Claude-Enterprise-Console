namespace ClaudeEnterprise.Api.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// When non-empty, calls may authenticate with X-API-Key. Leave empty to disable
    /// API-key auth (e.g. JWT-only deployments or dev).
    /// </summary>
    public IReadOnlyList<string> ApiKeys { get; init; } = Array.Empty<string>();

    public string HeaderName { get; init; } = "X-API-Key";

    /// <summary>OIDC / JWT bearer configuration. Enabled when <see cref="JwtOptions.Authority"/> is set.</summary>
    public JwtOptions Jwt { get; init; } = new();
}

public sealed class JwtOptions
{
    public string? Authority { get; init; }
    public string? Audience { get; init; }
    public bool RequireHttpsMetadata { get; init; } = true;
}

