using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnabDrive.Web.Domain;
using SnabDrive.Web.Services;

namespace SnabDrive.Web.Api;

/// <summary>Справочники. Чтение — всем авторизованным, изменение — только администратору.</summary>
[ApiController]
[Route("api/dictionaries")]
[Authorize]
[Produces("application/json")]
public class DictionaryController : ControllerBase
{
    private readonly IDictionaryService _dictionaries;

    public DictionaryController(IDictionaryService dictionaries)
    {
        _dictionaries = dictionaries;
    }

    [HttpGet("types-of-purchase")]
    public async Task<IActionResult> TypesOfPurchase(CancellationToken cancellationToken) =>
        Ok(await _dictionaries.GetTypesOfPurchaseAsync(cancellationToken));

    [HttpGet("b2b-statuses")]
    public async Task<IActionResult> B2bStatuses(CancellationToken cancellationToken) =>
        Ok(await _dictionaries.GetB2BStatusesAsync(cancellationToken));

    [HttpGet("execution-statuses")]
    public async Task<IActionResult> ExecutionStatuses(CancellationToken cancellationToken) =>
        Ok(await _dictionaries.GetExecutionStatusesAsync(cancellationToken));

    [HttpPost("types-of-purchase")]
    [Authorize(Policy = AppPolicies.ManageSystem)]
    public async Task<IActionResult> CreateType([FromBody] DictionaryUpsertRequest request, CancellationToken cancellationToken) =>
        ToResult(await _dictionaries.CreateTypeOfPurchaseAsync(request.Name, request.ColorCode, User.ToActor(), cancellationToken));

    [HttpPut("types-of-purchase/{id:int}")]
    [Authorize(Policy = AppPolicies.ManageSystem)]
    public async Task<IActionResult> UpdateType(int id, [FromBody] DictionaryUpsertRequest request, CancellationToken cancellationToken) =>
        ToResult(await _dictionaries.UpdateTypeOfPurchaseAsync(id, request.Name, request.ColorCode, User.ToActor(), cancellationToken));

    [HttpDelete("types-of-purchase/{id:int}")]
    [Authorize(Policy = AppPolicies.ManageSystem)]
    public async Task<IActionResult> DeleteType(int id, CancellationToken cancellationToken) =>
        ToResult(await _dictionaries.DeleteTypeOfPurchaseAsync(id, User.ToActor(), cancellationToken));

    [HttpPost("b2b-statuses")]
    [Authorize(Policy = AppPolicies.ManageSystem)]
    public async Task<IActionResult> CreateB2b([FromBody] DictionaryUpsertRequest request, CancellationToken cancellationToken) =>
        ToResult(await _dictionaries.CreateB2BStatusAsync(request.Name, User.ToActor(), cancellationToken));

    [HttpPut("b2b-statuses/{id:int}")]
    [Authorize(Policy = AppPolicies.ManageSystem)]
    public async Task<IActionResult> UpdateB2b(int id, [FromBody] DictionaryUpsertRequest request, CancellationToken cancellationToken) =>
        ToResult(await _dictionaries.UpdateB2BStatusAsync(id, request.Name, User.ToActor(), cancellationToken));

    [HttpDelete("b2b-statuses/{id:int}")]
    [Authorize(Policy = AppPolicies.ManageSystem)]
    public async Task<IActionResult> DeleteB2b(int id, CancellationToken cancellationToken) =>
        ToResult(await _dictionaries.DeleteB2BStatusAsync(id, User.ToActor(), cancellationToken));

    [HttpPost("execution-statuses")]
    [Authorize(Policy = AppPolicies.ManageSystem)]
    public async Task<IActionResult> CreateExecution([FromBody] DictionaryUpsertRequest request, CancellationToken cancellationToken) =>
        ToResult(await _dictionaries.CreateExecutionStatusAsync(request.Name, User.ToActor(), cancellationToken));

    [HttpPut("execution-statuses/{id:int}")]
    [Authorize(Policy = AppPolicies.ManageSystem)]
    public async Task<IActionResult> UpdateExecution(int id, [FromBody] DictionaryUpsertRequest request, CancellationToken cancellationToken) =>
        ToResult(await _dictionaries.UpdateExecutionStatusAsync(id, request.Name, User.ToActor(), cancellationToken));

    [HttpDelete("execution-statuses/{id:int}")]
    [Authorize(Policy = AppPolicies.ManageSystem)]
    public async Task<IActionResult> DeleteExecution(int id, CancellationToken cancellationToken) =>
        ToResult(await _dictionaries.DeleteExecutionStatusAsync(id, User.ToActor(), cancellationToken));

    private IActionResult ToResult(Result<DictionaryItemDto> result)
    {
        if (result.Success && result.Value is not null)
        {
            return Ok(result.Value);
        }

        return BadRequest(new { error = result.Error, fieldErrors = result.FieldErrors });
    }

    private IActionResult ToResult(Result result) =>
        result.Success ? NoContent() : BadRequest(new { error = result.Error });
}

/// <summary>Тело запроса на изменение справочника.</summary>
public sealed class DictionaryUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ColorCode { get; set; }
}
