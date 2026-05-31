using Anthropic;
using Anthropic.Core;
using ClaudeEnterprise.Application.Chat;
using ClaudeEnterprise.Application.Configuration;
using ClaudeEnterprise.Application.Observability;
using ClaudeEnterprise.Domain.Catalog;
using ClaudeEnterprise.Domain.Chat;
using ClaudeEnterprise.Infrastructure.Anthropic;
using ClaudeEnterprise.Infrastructure.Catalog;
using ClaudeEnterprise.Infrastructure.Observability;
using ClaudeEnterprise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClaudeEnterprise.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey)
                    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")),
                "Anthropic API key must be provided via configuration or ANTHROPIC_API_KEY env var.")
            .ValidateOnStart();

        services.AddSingleton<AnthropicClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            var apiKey = !string.IsNullOrWhiteSpace(opts.ApiKey)
                ? opts.ApiKey
                : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!;

            var clientOptions = new ClientOptions
            {
                APIKey = apiKey,
                MaxRetries = opts.MaxRetries,
                Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds),
            };
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                clientOptions.BaseUrl = new Uri(opts.BaseUrl);

            return new AnthropicClient(clientOptions);
        });

        services.AddSingleton<IModelCatalog, StaticModelCatalog>();
        services.AddSingleton<IUsageTracker, InMemoryUsageTracker>();
        services.AddSingleton<AnthropicResiliencePipeline>();
        services.AddScoped<IChatService, AnthropicChatService>();

        // Persistence
        services
            .AddOptions<PersistenceOptions>()
            .Bind(configuration.GetSection(PersistenceOptions.SectionName));

        var persistence = configuration.GetSection(PersistenceOptions.SectionName).Get<PersistenceOptions>()
            ?? new PersistenceOptions();

        if (string.Equals(persistence.Provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IConversationRepository, InMemoryConversationRepository>();
        }
        else
        {
            var cs = !string.IsNullOrWhiteSpace(persistence.ConnectionString)
                ? persistence.ConnectionString
                : "Data Source=claude-enterprise.db";
            services.AddDbContext<ChatDbContext>(opt => opt.UseSqlite(cs));
            services.AddScoped<IConversationRepository, EfConversationRepository>();
        }

        return services;
    }
}
