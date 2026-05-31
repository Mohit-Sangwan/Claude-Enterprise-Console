using Microsoft.AspNetCore.Authorization;

namespace ClaudeEnterprise.Api.Security;

public static class AuthorizationPolicies
{
    public const string ChatRead = "chat:read";
    public const string ChatWrite = "chat:write";
    public const string UsageRead = "usage:read";
    public const string UsageAdmin = "usage:admin";

    public static void Register(AuthorizationOptions o)
    {
        o.AddPolicy(ChatRead, p => p.RequireAuthenticatedUser().RequireAssertion(c => HasScope(c, ChatRead) || IsApiKey(c)));
        o.AddPolicy(ChatWrite, p => p.RequireAuthenticatedUser().RequireAssertion(c => HasScope(c, ChatWrite) || IsApiKey(c)));
        o.AddPolicy(UsageRead, p => p.RequireAuthenticatedUser().RequireAssertion(c => HasScope(c, UsageRead) || IsApiKey(c)));
        o.AddPolicy(UsageAdmin, p => p.RequireAuthenticatedUser().RequireAssertion(c => HasScope(c, UsageAdmin) || IsApiKey(c)));
    }

    private static bool HasScope(AuthorizationHandlerContext ctx, string scope)
    {
        foreach (var c in ctx.User.Claims)
        {
            if ((c.Type == "scope" || c.Type == "scp" || c.Type == "http://schemas.microsoft.com/identity/claims/scope")
                && c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsApiKey(AuthorizationHandlerContext ctx) =>
        string.Equals(ctx.User.Identity?.AuthenticationType, ApiKeyAuthenticationOptions.Scheme, StringComparison.Ordinal);
}
