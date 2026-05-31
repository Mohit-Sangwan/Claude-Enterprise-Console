namespace ClaudeEnterprise.Application.Configuration;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "claude-sonnet-4-5";
    public int MaxTokens { get; init; } = 1024;
    public double Temperature { get; init; } = 0.7;
    public int MaxRetries { get; init; } = 2;
    public int TimeoutSeconds { get; init; } = 120;
    public string? BaseUrl { get; init; }
    public string DefaultSystemPrompt { get; init; } =
        "You are a helpful enterprise AI assistant. Be concise, accurate, and cite assumptions.";
}
