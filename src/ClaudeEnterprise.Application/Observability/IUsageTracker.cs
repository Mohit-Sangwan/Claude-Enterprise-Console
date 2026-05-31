namespace ClaudeEnterprise.Application.Observability;

/// <summary>
/// Aggregates token consumption and estimated spend per model.
/// Implementations must be thread-safe.
/// </summary>
public interface IUsageTracker
{
    void Record(string model, long inputTokens, long outputTokens);
    UsageSnapshot Snapshot();
    void Reset();
}

public sealed record UsageSnapshot(
    DateTimeOffset Since,
    long TotalInputTokens,
    long TotalOutputTokens,
    decimal EstimatedCostUsd,
    IReadOnlyList<ModelUsage> Models);

public sealed record ModelUsage(
    string Model,
    long InputTokens,
    long OutputTokens,
    long Requests,
    decimal EstimatedCostUsd);
