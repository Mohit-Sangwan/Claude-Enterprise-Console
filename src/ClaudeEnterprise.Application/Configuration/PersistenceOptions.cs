namespace ClaudeEnterprise.Application.Configuration;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    /// <summary>"Sqlite" (default) or "InMemory".</summary>
    public string Provider { get; init; } = "Sqlite";

    /// <summary>Connection string; defaults to local file under content root.</summary>
    public string? ConnectionString { get; init; }
}
