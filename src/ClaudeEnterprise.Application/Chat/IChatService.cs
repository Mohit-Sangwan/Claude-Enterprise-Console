namespace ClaudeEnterprise.Application.Chat;

public interface IChatService
{
    Task<SendMessageResponse> SendAsync(SendMessageRequest request, CancellationToken ct);
    IAsyncEnumerable<StreamChunk> StreamAsync(SendMessageRequest request, CancellationToken ct);
}
