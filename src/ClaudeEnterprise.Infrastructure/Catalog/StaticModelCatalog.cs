using ClaudeEnterprise.Domain.Catalog;

namespace ClaudeEnterprise.Infrastructure.Catalog;

/// <summary>
/// Curated catalog of supported Claude models. Centralized here so the
/// front-end never hardcodes IDs and ops can add/retire models in one place.
/// </summary>
internal sealed class StaticModelCatalog : IModelCatalog
{
    // Pricing (USD per 1M tokens) reflects Anthropic's published rates; update via config when they change.
    private static readonly ClaudeModel[] Models = new[]
    {
        new ClaudeModel("claude-opus-4-5",          "Claude Opus 4.5",   "Opus",   "Flagship", 200_000, 16_000, false, true,
            "Highest reasoning capability. Use for complex multi-step analysis.", 15m, 75m),
        new ClaudeModel("claude-sonnet-4-5",        "Claude Sonnet 4.5", "Sonnet", "Balanced", 200_000,  8_192, true,  true,
            "Best balance of cost, latency, and quality. Default for chat.",       3m, 15m),
        new ClaudeModel("claude-haiku-4-5",         "Claude Haiku 4.5",  "Haiku",  "Fast",     200_000,  8_192, false, false,
            "Fastest and cheapest. Ideal for high-volume or low-latency calls.",   1m,  5m),
        new ClaudeModel("claude-3-7-sonnet-latest", "Claude 3.7 Sonnet", "Sonnet", "Legacy",   200_000,  8_192, false, false,
            "Previous-generation Sonnet, kept for backward-compatible workloads.", 3m, 15m),
        new ClaudeModel("claude-3-5-haiku-latest",  "Claude 3.5 Haiku",  "Haiku",  "Legacy",   200_000,  8_192, false, false,
            "Previous-generation Haiku.",                                       0.8m,  4m),
    };

    public IReadOnlyList<ClaudeModel> All() => Models;

    public ClaudeModel? Find(string id) =>
        Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public ClaudeModel GetDefault() =>
        Models.FirstOrDefault(m => m.IsDefault) ?? Models[0];
}
