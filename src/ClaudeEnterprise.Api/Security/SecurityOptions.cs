namespace ClaudeEnterprise.Api.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// When non-empty, every call to /api/* must present a matching key
    /// via the X-API-Key header. Leave empty to disable API key auth (dev).
    /// </summary>
    public IReadOnlyList<string> ApiKeys { get; init; } = Array.Empty<string>();

    public string HeaderName { get; init; } = "X-API-Key";
}
