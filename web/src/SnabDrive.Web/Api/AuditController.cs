using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnabDrive.Web.Domain;
using SnabDrive.Web.Services;

namespace SnabDrive.Web.Api;

/// <summary>Журнал изменений (только администратор).</summary>
[ApiController]
[Route("api/audit")]
[Authorize(Policy = AppPolicies.ManageSystem)]
[Produces("application/json")]
public class AuditController : ControllerBase
{
    private readonly IAuditService _audit;

    public AuditController(IAuditService audit)
    {
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> Get(
        [FromQuery] string? entityName,
        [FromQuery] string? action,
        [FromQuery] string? userName,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = new AuditQuery
        {
            EntityName = entityName,
            Action = action,
            UserName = userName,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await _audit.QueryAsync(query, cancellationToken));
    }
}
