using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ClaudeEnterprise.Api.Security;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "ApiKey";
}

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly SecurityOptions _security;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> opts,
        IOptions<SecurityOptions> security,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(opts, logger, encoder)
    {
        _security = security.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No keys configured → auth disabled; allow everything.
        if (_security.ApiKeys.Count == 0)
        {
            var anon = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "anonymous") }, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(anon), Scheme.Name)));
        }

        if (!Request.Headers.TryGetValue(_security.HeaderName, out var provided) || string.IsNullOrWhiteSpace(provided))
            return Task.FromResult(AuthenticateResult.Fail("API key missing."));

        if (!_security.ApiKeys.Any(k => CryptographicEquals(k, provided!)))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var id = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "api-key-client") }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(id), Scheme.Name)));
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
