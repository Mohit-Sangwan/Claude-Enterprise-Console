using System.Diagnostics;
using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using ClaudeEnterprise.Application.Chat;
using ClaudeEnterprise.Application.Configuration;
using ClaudeEnterprise.Application.Observability;
using ClaudeEnterprise.Domain.Catalog;
using ClaudeEnterprise.Domain.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaudeEnterprise.Infrastructure.Anthropic;

/// <summary>
/// Anthropic-backed chat service. Wraps the official Anthropic C# SDK
/// (NuGet: Anthropic v10) with conversation persistence and streaming.
/// </summary>
internal sealed class AnthropicChatService : IChatService
{
    internal static readonly ActivitySource ActivitySource = new("ClaudeEnterprise.Anthropic", "1.0.0");

    private readonly AnthropicClient _client;
    private readonly IConversationRepository _repo;
    private readonly IModelCatalog _catalog;
    private readonly IUsageTracker _usage;
    private readonly AnthropicResiliencePipeline _resilience;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicChatService> _logger;

    public AnthropicChatService(
        AnthropicClient client,
        IConversationRepository repo,
        IModelCatalog catalog,
        IUsageTracker usage,
        AnthropicResiliencePipeline resilience,
        IOptions<AnthropicOptions> options,
        ILogger<AnthropicChatService> logger)
    {
        _client = client;
        _repo = repo;
        _catalog = catalog;
        _usage = usage;
        _resilience = resilience;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SendMessageResponse> SendAsync(SendMessageRequest request, CancellationToken ct)
    {
        var (conversation, sdkMessages) = await PrepareAsync(request, ct).ConfigureAwait(false);
        var model = ResolveModel(request.Model);

        _logger.LogInformation("Claude.Send conv={ConversationId} model={Model} msgs={Count}",
            conversation.Id, model, sdkMessages.Count);

        using var activity = ActivitySource.StartActivity("anthropic.messages.create", ActivityKind.Client);
        activity?.SetTag("anthropic.model", model);
        activity?.SetTag("anthropic.conversation_id", conversation.Id);
        activity?.SetTag("anthropic.message_count", sdkMessages.Count);

        var response = await _resilience.Pipeline.ExecuteAsync(
            async token => await _client.Messages.Create(new MessageCreateParams
            {
                Model = model,
                MaxTokens = request.MaxTokens ?? _options.MaxTokens,
                Temperature = request.Temperature ?? _options.Temperature,
                System = request.System ?? _options.DefaultSystemPrompt,
                Messages = sdkMessages,
            }, token).ConfigureAwait(false),
            ct).ConfigureAwait(false);

        var text = ExtractText(response);
        var updated = conversation
            .AppendMessage(new ChatMessage(ChatRole.User, request.Message))
            .AppendMessage(new ChatMessage(ChatRole.Assistant, text));

        await _repo.SaveAsync(updated, ct).ConfigureAwait(false);

        var inputTokens = (int)(response.Usage?.InputTokens ?? 0);
        var outputTokens = (int)(response.Usage?.OutputTokens ?? 0);
        _usage.Record(model, inputTokens, outputTokens);
        activity?.SetTag("anthropic.usage.input_tokens", inputTokens);
        activity?.SetTag("anthropic.usage.output_tokens", outputTokens);

        return new SendMessageResponse(
            updated.Id,
            text,
            inputTokens,
            outputTokens,
            model);
    }

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        SendMessageRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var (conversation, sdkMessages) = await PrepareAsync(request, ct).ConfigureAwait(false);
        var model = ResolveModel(request.Model);

        yield return new StreamChunk("start", null, conversation.Id, null);

        using var activity = ActivitySource.StartActivity("anthropic.messages.stream", ActivityKind.Client);
        activity?.SetTag("anthropic.model", model);
        activity?.SetTag("anthropic.conversation_id", conversation.Id);

        var stream = _client.Messages.CreateStreaming(new MessageCreateParams
        {
            Model = model,
            MaxTokens = request.MaxTokens ?? _options.MaxTokens,
            Temperature = request.Temperature ?? _options.Temperature,
            System = request.System ?? _options.DefaultSystemPrompt,
            Messages = sdkMessages,
        }, ct);

        var buffer = new System.Text.StringBuilder();
        var enumerator = stream.GetAsyncEnumerator(ct);
        string? errorMsg = null;
        long inputTokens = 0, outputTokens = 0;
        try
        {
            while (true)
            {
                RawMessageStreamEvent evt;
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    yield break;
                }
                catch (AnthropicException ex)
                {
                    _logger.LogError(ex, "Claude.Stream failed conv={ConversationId}", conversation.Id);
                    errorMsg = ExtractErrorMessage(ex);
                    break;
                }
                if (!hasNext) break;
                evt = enumerator.Current;

                TryAccumulateUsage(evt, ref inputTokens, ref outputTokens);

                var delta = ExtractDelta(evt);
                if (string.IsNullOrEmpty(delta)) continue;
                buffer.Append(delta);
                yield return new StreamChunk("delta", delta, conversation.Id, null);
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        if (errorMsg is not null)
        {
            yield return new StreamChunk("error", null, conversation.Id, errorMsg);
            yield break;
        }

        var finalText = buffer.ToString();
        var updated = conversation
            .AppendMessage(new ChatMessage(ChatRole.User, request.Message))
            .AppendMessage(new ChatMessage(ChatRole.Assistant, finalText));
        await _repo.SaveAsync(updated, ct).ConfigureAwait(false);

        _usage.Record(model, inputTokens, outputTokens);

        yield return new StreamChunk("done", null, updated.Id, null);
    }

    private static string ExtractErrorMessage(AnthropicException ex)
    {
        // The SDK serializes the upstream JSON into Message; try to surface the human message.
        var msg = ex.Message ?? "Upstream error.";
        var idx = msg.IndexOf("\"message\":\"", StringComparison.Ordinal);
        if (idx < 0) return msg;
        var start = idx + "\"message\":\"".Length;
        var end = msg.IndexOf('"', start);
        return end > start ? msg[start..end] : msg;
    }

    private string ResolveModel(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var found = _catalog.Find(requested);
            if (found is not null) return found.Id;
            _logger.LogWarning("Unknown model {Model} requested; falling back to default.", requested);
        }
        return _catalog.GetDefault().Id;
    }

    private async Task<(Conversation conv, List<MessageParam> sdkMessages)> PrepareAsync(
        SendMessageRequest request, CancellationToken ct)
    {
        var conv = request.ConversationId is { } id
            ? await _repo.GetAsync(id, ct).ConfigureAwait(false) ?? Conversation.NewConversation(Truncate(request.Message, 60))
            : Conversation.NewConversation(Truncate(request.Message, 60));

        var history = new List<MessageParam>(conv.Messages.Count + 1);
        foreach (var m in conv.Messages)
            history.Add(ToSdkMessage(m.Role, m.Content));
        history.Add(ToSdkMessage(ChatRole.User, request.Message));
        return (conv, history);
    }

    private static MessageParam ToSdkMessage(ChatRole role, string content) => new()
    {
        Role = role == ChatRole.Assistant ? "assistant" : "user",
        Content = content,
    };

    private static string ExtractText(Message message)
    {
        if (message.Content is null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var t) && t is not null)
                sb.Append(t.Text);
        }
        return sb.ToString();
    }

    private static string? ExtractDelta(RawMessageStreamEvent evt)
    {
        if (!evt.TryPickContentBlockDelta(out var deltaEvt) || deltaEvt is null) return null;
        if (deltaEvt.Delta.TryPickText(out var td) && td is not null) return td.Text;
        return null;
    }

    private static void TryAccumulateUsage(RawMessageStreamEvent evt, ref long input, ref long output)
    {
        // message_start carries initial usage (input tokens); message_delta carries final output tokens.
        if (evt.TryPickStart(out var startEvt) && startEvt?.Message?.Usage is { } u0)
        {
            input += u0.InputTokens;
            output += u0.OutputTokens;
        }
        else if (evt.TryPickDelta(out var deltaEvt) && deltaEvt?.Usage is { } u1)
        {
            if (u1.InputTokens is { } extraIn) input += extraIn;
            output += u1.OutputTokens;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
