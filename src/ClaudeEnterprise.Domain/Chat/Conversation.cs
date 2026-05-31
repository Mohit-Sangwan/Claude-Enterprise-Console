namespace ClaudeEnterprise.Domain.Chat;

public enum ChatRole
{
    System,
    User,
    Assistant
}

public sealed record ChatMessage(ChatRole Role, string Content);

public sealed record Conversation(
    Guid Id,
    string Title,
    IReadOnlyList<ChatMessage> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ETag,
    bool IsDeleted = false)
{
    public Conversation AppendMessage(ChatMessage message)
    {
        var next = new List<ChatMessage>(Messages) { message };
        var now = DateTimeOffset.UtcNow;
        return this with { Messages = next, UpdatedAt = now, ETag = MakeETag(Id, now, next.Count) };
    }

    public static Conversation NewConversation(string title)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        return new Conversation(id, title, Array.Empty<ChatMessage>(), now, now, MakeETag(id, now, 0));
    }

    public static string MakeETag(Guid id, DateTimeOffset updatedAt, int messageCount)
        => $"\"{Convert.ToHexString(BitConverter.GetBytes(updatedAt.UtcTicks ^ id.GetHashCode() ^ messageCount))}\"";
}
