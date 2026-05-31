using Asp.Versioning;
using ClaudeEnterprise.Api.Security;
using ClaudeEnterprise.Application.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeEnterprise.Api.Controllers;

[ApiController]
[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class UsageController : ControllerBase
{
    private readonly IUsageTracker _tracker;
    public UsageController(IUsageTracker tracker) => _tracker = tracker;

    /// <summary>Cumulative token usage and estimated spend per model since process start (or last reset).</summary>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.UsageRead)]
    [ProducesResponseType(typeof(UsageSnapshot), StatusCodes.Status200OK)]
    public ActionResult<UsageSnapshot> Get() => Ok(_tracker.Snapshot());

    /// <summary>Reset all in-memory usage counters. Intended for admin/automation use.</summary>
    [HttpPost("reset")]
    [Authorize(Policy = AuthorizationPolicies.UsageAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Reset()
    {
        _tracker.Reset();
        return NoContent();
    }
}
