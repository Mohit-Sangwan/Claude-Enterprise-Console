using System.Text.RegularExpressions;

namespace ClaudeEnterprise.Api.Middleware;

/// <summary>
/// Best-effort PII / secret scrubber for audit logs and structured properties.
/// Patterns are intentionally conservative — false positives are preferred to leaking PII.
/// </summary>
public static partial class PiiRedactor
{
    private const string Mask = "[REDACTED]";

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:\d[ -]*?){13,19}\b", RegexOptions.Compiled)]
    private static partial Regex CardRegex();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex SsnRegex();

    [GeneratedRegex(@"sk-[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled)]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9._\-]+", RegexOptions.Compiled)]
    private static partial Regex BearerRegex();

    public static string Scrub(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        var s = EmailRegex().Replace(input, Mask);
        s = SsnRegex().Replace(s, Mask);
        s = CardRegex().Replace(s, Mask);
        s = ApiKeyRegex().Replace(s, Mask);
        s = BearerRegex().Replace(s, "Bearer " + Mask);
        return s;
    }
}
