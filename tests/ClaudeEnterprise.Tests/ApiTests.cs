using System.Net;
using System.Net.Http.Json;
using ClaudeEnterprise.Domain.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClaudeEnterprise.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(c =>
        {
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"] = "test-key",
                ["Persistence:Provider"] = "InMemory",
                ["Security:ApiKeys:0"] = "test-secret",
            });
        });
        return base.CreateHost(builder);
    }
}

public sealed class ModelsEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public ModelsEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_models_returns_catalog()
    {
        var client = _factory.CreateClient();
        var models = await client.GetFromJsonAsync<ClaudeModel[]>("/api/models");
        models.Should().NotBeNull();
        models!.Length.Should().BeGreaterThan(0);
        models.Should().Contain(m => m.Id.Contains("sonnet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Health_ready_returns_healthy()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health/ready");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Correlation_id_is_echoed()
    {
        var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/models");
        req.Headers.Add("X-Correlation-Id", "trace-xyz-1");
        var resp = await client.SendAsync(req);
        resp.Headers.GetValues("X-Correlation-Id").Should().Contain("trace-xyz-1");
    }
}

public sealed class AuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AuthTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Usage_requires_api_key()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/usage");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Usage_succeeds_with_api_key()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-secret");
        var resp = await client.GetAsync("/api/usage");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

public sealed class SecurityHeaderTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public SecurityHeaderTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Responses_include_security_headers()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/models");
        resp.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        resp.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
    }
}
