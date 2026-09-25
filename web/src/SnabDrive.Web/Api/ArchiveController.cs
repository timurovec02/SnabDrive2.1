using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnabDrive.Web.Domain;
using SnabDrive.Web.Services;

namespace SnabDrive.Web.Api;

/// <summary>Архив реестра.</summary>
[ApiController]
[Route("api/archive")]
[Authorize(Policy = AppPolicies.ManageArchive)]
[Produces("application/json")]
public class ArchiveController : ControllerBase
{
    private readonly IRegistryService _registry;

    public ArchiveController(IRegistryService registry)
    {
        _registry = registry;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<RegistryRowDto>>> Get(
        [FromQuery] RegistryQueryRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _registry.QueryArchiveAsync(request.ToQuery(), cancellationToken));
    }

    [HttpPost("{archiveId:int}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Restore(int archiveId, CancellationToken cancellationToken)
    {
        var result = await _registry.RestoreAsync(archiveId, User.ToActor(), cancellationToken);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpPut("{archiveId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        int archiveId, [FromBody] RegeditUpsertRequest request, CancellationToken cancellationToken)
    {
        var result = await _registry.UpdateArchiveAsync(archiveId, request, User.ToActor(), cancellationToken);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error, fieldErrors = result.FieldErrors });
    }

    [HttpDelete("{archiveId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(int archiveId, CancellationToken cancellationToken)
    {
        var result = await _registry.DeleteArchiveAsync(archiveId, User.ToActor(), cancellationToken);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }
}
