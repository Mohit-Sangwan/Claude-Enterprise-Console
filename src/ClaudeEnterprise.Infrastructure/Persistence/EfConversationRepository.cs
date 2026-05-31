using ClaudeEnterprise.Domain.Chat;
using Microsoft.EntityFrameworkCore;

namespace ClaudeEnterprise.Infrastructure.Persistence;

internal sealed class EfConversationRepository : IConversationRepository
{
    private readonly ChatDbContext _db;

    public EfConversationRepository(ChatDbContext db) => _db = db;

    public async Task<Conversation?> GetAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.Messages.OrderBy(m => m.Position))
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(int take, CancellationToken ct)
    {
        var entities = await _db.Conversations
            .AsNoTracking()
            .OrderByDescending(c => c.UpdatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Include(c => c.Messages.OrderBy(m => m.Position))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return entities.ConvertAll(Map);
    }

    public async Task<ConversationPage> PageAsync(int pageSize, string? cursor, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.Conversations.AsNoTracking().OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id).AsQueryable();

        if (TryParseCursor(cursor, out var afterTs, out var afterId))
        {
            query = query.Where(c => c.UpdatedAt < afterTs
                || (c.UpdatedAt == afterTs && c.Id.CompareTo(afterId) > 0));
        }

        var total = await _db.Conversations.AsNoTracking().CountAsync(ct).ConfigureAwait(false);
        var page = await query
            .Take(pageSize + 1)
            .Include(c => c.Messages.OrderBy(m => m.Position))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (page.Count > pageSize)
        {
            var last = page[pageSize - 1];
            nextCursor = MakeCursor(last.UpdatedAt, last.Id);
            page.RemoveAt(pageSize);
        }
        return new ConversationPage(page.ConvertAll(Map), nextCursor, total);
    }

    public async Task SaveAsync(Conversation conversation, CancellationToken ct)
    {
        var existing = await _db.Conversations
            .IgnoreQueryFilters()
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversation.Id, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.Conversations.Add(ToEntity(conversation));
        }
        else
        {
            existing.Title = conversation.Title;
            existing.UpdatedAt = conversation.UpdatedAt;
            existing.ETag = conversation.ETag;
            existing.IsDeleted = conversation.IsDeleted;
            _db.Messages.RemoveRange(existing.Messages);
            existing.Messages = MapMessages(conversation);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (entity is null || entity.IsDeleted) return false;
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static Conversation Map(ConversationEntity e) => new(
        e.Id,
        e.Title,
        e.Messages.OrderBy(m => m.Position).Select(m => new ChatMessage(m.Role, m.Content)).ToList(),
        e.CreatedAt,
        e.UpdatedAt,
        e.ETag,
        e.IsDeleted);

    private static ConversationEntity ToEntity(Conversation c) => new()
    {
        Id = c.Id,
        Title = c.Title,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
        ETag = c.ETag,
        IsDeleted = c.IsDeleted,
        Messages = MapMessages(c),
    };

    private static List<MessageEntity> MapMessages(Conversation c)
    {
        var list = new List<MessageEntity>(c.Messages.Count);
        for (var i = 0; i < c.Messages.Count; i++)
        {
            var m = c.Messages[i];
            list.Add(new MessageEntity
            {
                ConversationId = c.Id,
                Position = i,
                Role = m.Role,
                Content = m.Content,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        return list;
    }

    private static string MakeCursor(DateTimeOffset ts, Guid id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{ts.UtcTicks}|{id}"));

    private static bool TryParseCursor(string? cursor, out DateTimeOffset ts, out Guid id)
    {
        ts = default; id = default;
        if (string.IsNullOrWhiteSpace(cursor)) return false;
        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            if (parts.Length != 2) return false;
            if (!long.TryParse(parts[0], out var ticks)) return false;
            if (!Guid.TryParse(parts[1], out id)) return false;
            ts = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException) { return false; }
    }
}
