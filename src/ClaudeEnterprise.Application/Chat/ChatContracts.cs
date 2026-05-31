namespace ClaudeEnterprise.Application.Chat;

public sealed record SendMessageRequest(
    Guid? ConversationId,
    string Message,
    string? System,
    string? Model,
    int? MaxTokens,
    double? Temperature);

public sealed record SendMessageResponse(
    Guid ConversationId,
    string Reply,
    int InputTokens,
    int OutputTokens,
    string Model);

public sealed record StreamChunk(
    string Type,
    string? Delta,
    Guid? ConversationId,
    string? Error);
