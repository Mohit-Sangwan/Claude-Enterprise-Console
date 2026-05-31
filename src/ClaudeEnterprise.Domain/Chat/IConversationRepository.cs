namespace ClaudeEnterprise.Domain.Chat;

public sealed record ConversationPage(
    IReadOnlyList<Conversation> Items,
    string? NextCursor,
    int TotalCount);

public interface IConversationRepository
{
    Task<Conversation?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Conversation>> ListAsync(int take, CancellationToken ct);
    Task<ConversationPage> PageAsync(int pageSize, string? cursor, CancellationToken ct);
    Task SaveAsync(Conversation conversation, CancellationToken ct);
    /// <summary>Soft-delete by default. Returns false if not found.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
