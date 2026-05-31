using System.Collections.Concurrent;
using ClaudeEnterprise.Domain.Chat;

namespace ClaudeEnterprise.Infrastructure.Persistence;

/// <summary>
/// Thread-safe in-memory repository. Swap with EF Core / Cosmos / Redis in production.
/// </summary>
internal sealed class InMemoryConversationRepository : IConversationRepository
{
    private readonly ConcurrentDictionary<Guid, Conversation> _store = new();

    public Task<Conversation?> GetAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_store.TryGetValue(id, out var c) && !c.IsDeleted ? c : null);

    public Task<IReadOnlyList<Conversation>> ListAsync(int take, CancellationToken ct)
    {
        IReadOnlyList<Conversation> list = _store.Values
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToList();
        return Task.FromResult(list);
    }

    public Task<ConversationPage> PageAsync(int pageSize, string? cursor, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = string.IsNullOrEmpty(cursor) || !int.TryParse(cursor, out var s) ? 0 : s;
        var items = _store.Values
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id)
            .Skip(skip)
            .Take(pageSize + 1)
            .ToList();

        string? next = null;
        if (items.Count > pageSize)
        {
            next = (skip + pageSize).ToString(System.Globalization.CultureInfo.InvariantCulture);
            items.RemoveAt(pageSize);
        }
        return Task.FromResult(new ConversationPage(items, next, _store.Values.Count(c => !c.IsDeleted)));
    }

    public Task SaveAsync(Conversation conversation, CancellationToken ct)
    {
        _store[conversation.Id] = conversation;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!_store.TryGetValue(id, out var existing) || existing.IsDeleted) return Task.FromResult(false);
        _store[id] = existing with { IsDeleted = true, UpdatedAt = DateTimeOffset.UtcNow };
        return Task.FromResult(true);
    }
}
