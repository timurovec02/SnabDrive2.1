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
    private readonly IColumnAccessService _columnAccess;

    public ArchiveController(IRegistryService registry, IColumnAccessService columnAccess)
    {
        _registry = registry;
        _columnAccess = columnAccess;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<RegistryRowDto>>> Get(
        [FromQuery] RegistryQueryRequest request, CancellationToken cancellationToken)
    {
        var access = await _columnAccess.GetAsync(
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "-", cancellationToken);

        return Ok(await _registry.QueryArchiveAsync(request.ToQuery(), access, cancellationToken));
    }

    [HttpPost("{archiveId:int}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Restore(int archiveId, CancellationToken cancellationToken)
    {
        var result = await _registry.RestoreAsync(archiveId, await User.ToActorAsync(_columnAccess, cancellationToken), cancellationToken);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpPut("{archiveId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        int archiveId, [FromBody] RegeditUpsertRequest request, CancellationToken cancellationToken)
    {
        var result = await _registry.UpdateArchiveAsync(archiveId, request, await User.ToActorAsync(_columnAccess, cancellationToken), cancellationToken);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error, fieldErrors = result.FieldErrors });
    }

    [HttpDelete("{archiveId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(int archiveId, CancellationToken cancellationToken)
    {
        var result = await _registry.DeleteArchiveAsync(archiveId, await User.ToActorAsync(_columnAccess, cancellationToken), cancellationToken);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }
}
