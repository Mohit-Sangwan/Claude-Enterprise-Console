using System.Collections.Concurrent;
using ClaudeEnterprise.Application.Observability;
using ClaudeEnterprise.Domain.Catalog;

namespace ClaudeEnterprise.Infrastructure.Observability;

internal sealed class InMemoryUsageTracker : IUsageTracker
{
    private readonly IModelCatalog _catalog;
    private readonly ConcurrentDictionary<string, Counter> _byModel = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _since = DateTimeOffset.UtcNow;

    public InMemoryUsageTracker(IModelCatalog catalog) => _catalog = catalog;

    public void Record(string model, long inputTokens, long outputTokens)
    {
        if (string.IsNullOrWhiteSpace(model)) return;
        var c = _byModel.GetOrAdd(model, _ => new Counter());
        Interlocked.Add(ref c.Input, inputTokens);
        Interlocked.Add(ref c.Output, outputTokens);
        Interlocked.Increment(ref c.Requests);
    }

    public UsageSnapshot Snapshot()
    {
        var list = new List<ModelUsage>(_byModel.Count);
        long totalIn = 0, totalOut = 0;
        decimal totalCost = 0m;
        foreach (var kv in _byModel)
        {
            var m = _catalog.Find(kv.Key);
            var cost = m is null ? 0m
                : (kv.Value.Input * m.InputCostPerMTok + kv.Value.Output * m.OutputCostPerMTok) / 1_000_000m;
            totalIn += kv.Value.Input;
            totalOut += kv.Value.Output;
            totalCost += cost;
            list.Add(new ModelUsage(kv.Key, kv.Value.Input, kv.Value.Output, kv.Value.Requests, decimal.Round(cost, 4)));
        }
        return new UsageSnapshot(_since, totalIn, totalOut, decimal.Round(totalCost, 4),
            list.OrderByDescending(x => x.InputTokens + x.OutputTokens).ToList());
    }

    public void Reset()
    {
        _byModel.Clear();
        _since = DateTimeOffset.UtcNow;
    }

    private sealed class Counter
    {
        public long Input;
        public long Output;
        public long Requests;
    }
}
