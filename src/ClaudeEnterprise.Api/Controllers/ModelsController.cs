using Asp.Versioning;
using ClaudeEnterprise.Domain.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeEnterprise.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class ModelsController : ControllerBase
{
    private readonly IModelCatalog _catalog;

    public ModelsController(IModelCatalog catalog) => _catalog = catalog;

    /// <summary>List Claude models available for selection in the UI.</summary>
    [HttpGet]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    [ProducesResponseType(typeof(IReadOnlyList<ClaudeModel>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ClaudeModel>> List() => Ok(_catalog.All());
}
