using System.Text;
using System.Text.Json;
using Asp.Versioning;
using ClaudeEnterprise.Api.Security;
using ClaudeEnterprise.Application.Chat;
using ClaudeEnterprise.Domain.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeEnterprise.Api.Controllers;

[ApiController]
[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class ChatController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IChatService _chat;
    private readonly IConversationRepository _repo;

    public ChatController(IChatService chat, IConversationRepository repo)
    {
        _chat = chat;
        _repo = repo;
    }

    /// <summary>Non-streaming completion.</summary>
    [HttpPost("messages")]
    [Authorize(Policy = AuthorizationPolicies.ChatWrite)]
    [ProducesResponseType(typeof(SendMessageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SendMessageResponse>> Send(
        [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        var result = await _chat.SendAsync(request, ct).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>Server-Sent Events streaming endpoint.</summary>
    [HttpPost("messages/stream")]
    [Authorize(Policy = AuthorizationPolicies.ChatWrite)]
    public async Task Stream([FromBody] SendMessageRequest request, CancellationToken ct)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.Headers["Connection"] = "keep-alive";

        await foreach (var chunk in _chat.StreamAsync(request, ct).ConfigureAwait(false))
        {
            var payload = JsonSerializer.Serialize(chunk, Json);
            var bytes = Encoding.UTF8.GetBytes($"event: {chunk.Type}\ndata: {payload}\n\n");
            await Response.Body.WriteAsync(bytes, ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    [HttpGet("conversations")]
    [Authorize(Policy = AuthorizationPolicies.ChatRead)]
    [ProducesResponseType(typeof(ConversationPage), StatusCodes.Status200OK)]
    public async Task<ActionResult<ConversationPage>> List(
        [FromQuery] int pageSize = 25,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
        => Ok(await _repo.PageAsync(pageSize, cursor, ct).ConfigureAwait(false));

    [HttpGet("conversations/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ChatRead)]
    [ProducesResponseType(typeof(Conversation), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Conversation>> Get(Guid id, CancellationToken ct)
    {
        var conv = await _repo.GetAsync(id, ct).ConfigureAwait(false);
        if (conv is null) return NotFound();

        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == conv.ETag)
            return StatusCode(StatusCodes.Status304NotModified);

        Response.Headers.ETag = conv.ETag;
        return Ok(conv);
    }

    [HttpDelete("conversations/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ChatWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await _repo.DeleteAsync(id, ct).ConfigureAwait(false) ? NoContent() : NotFound();
}
