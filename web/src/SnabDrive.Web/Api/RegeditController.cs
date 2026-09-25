using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnabDrive.Web.Domain;
using SnabDrive.Web.Services;
using SnabDrive.Web.Services.Export;

namespace SnabDrive.Web.Api;

/// <summary>CRUD основной таблицы реестра.</summary>
[ApiController]
[Route("api/regedit")]
[Authorize]
[Produces("application/json")]
public class RegeditController : ControllerBase
{
    private readonly IRegistryService _registry;
    private readonly IDictionaryService _dictionaries;
    private readonly IColumnAccessService _columnAccess;

    public RegeditController(IRegistryService registry, IDictionaryService dictionaries, IColumnAccessService columnAccess)
    {
        _registry = registry;
        _dictionaries = dictionaries;
        _columnAccess = columnAccess;
    }

    /// <summary>Список записей с поиском, сортировкой, фильтрами и пагинацией.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<RegistryRowDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<RegistryRowDto>>> Get(
        [FromQuery] RegistryQueryRequest request, CancellationToken cancellationToken)
    {
        var access = await _columnAccess.GetAsync(CurrentUserId, cancellationToken);
        var result = await _registry.QueryAsync(request.ToQuery(), access, cancellationToken);
        return Ok(result);
    }

    /// <summary>Выгрузка текущей выборки в CSV.</summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(
        [FromQuery] RegistryQueryRequest request, CancellationToken cancellationToken)
    {
        var query = request.ToQuery() with { Page = 1, PageSize = RegistryQuery.MaxPageSize };
        var access = await _columnAccess.GetAsync(CurrentUserId, cancellationToken);
        var page = await _registry.QueryAsync(query, access, cancellationToken);

        var csv = CsvExporter.Export(page.Items, RegistryColumns.DefaultVisible.ToList());
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

        return File(bytes, "text/csv; charset=utf-8", $"regedit-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    /// <summary>Одна запись по Id.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RegistryRowDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegistryRowDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var row = await _registry.GetByIdAsync(id, await _columnAccess.GetAsync(CurrentUserId, cancellationToken), cancellationToken);
        return row is null ? NotFound() : Ok(row);
    }

    /// <summary>Форма записи для редактирования.</summary>
    [HttpGet("{id:int}/edit-model")]
    [ProducesResponseType(typeof(RegeditUpsertRequest), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegeditUpsertRequest>> GetEditModel(int id, CancellationToken cancellationToken)
    {
        var model = await _registry.GetEditModelAsync(id, await _columnAccess.GetAsync(CurrentUserId, cancellationToken), cancellationToken);
        return model is null ? NotFound() : Ok(model);
    }

    /// <summary>Создать запись.</summary>
    [HttpPost]
    [Authorize(Policy = AppPolicies.WriteRegistry)]
    [ProducesResponseType(typeof(RegistryRowDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegistryRowDto>> Create(
        [FromBody] RegeditUpsertRequest request, CancellationToken cancellationToken)
    {
        var result = await _registry.CreateAsync(request, await User.ToActorAsync(_columnAccess, cancellationToken), cancellationToken);
        return ToActionResult(result, StatusCodes.Status201Created);
    }

    /// <summary>Изменить запись.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = AppPolicies.WriteRegistry)]
    [ProducesResponseType(typeof(RegistryRowDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegistryRowDto>> Update(
        int id, [FromBody] RegeditUpsertRequest request, CancellationToken cancellationToken)
    {
        var result = await _registry.UpdateAsync(id, request, await User.ToActorAsync(_columnAccess, cancellationToken), cancellationToken);
        return ToActionResult(result, StatusCodes.Status200OK);
    }

    /// <summary>Удалить запись.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = AppPolicies.WriteRegistry)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await _registry.DeleteAsync(id, await User.ToActorAsync(_columnAccess, cancellationToken), cancellationToken);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    /// <summary>Переместить запись в архив (только администратор).</summary>
    [HttpPost("{id:int}/archive")]
    [Authorize(Policy = AppPolicies.ManageArchive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Archive(int id, CancellationToken cancellationToken)
    {
        var result = await _registry.ArchiveAsync(id, await User.ToActorAsync(_columnAccess, cancellationToken), cancellationToken);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    /// <summary>Поставить или снять подсветку ячейки.</summary>
    [HttpPut("{id:int}/cell-color")]
    [Authorize(Policy = AppPolicies.WriteRegistry)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetCellColor(
        int id, [FromBody] CellColorRequest request, CancellationToken cancellationToken)
    {
        var result = await _registry.SetCellColorAsync(id, request.ColumnName, request.ColorCode,
            await User.ToActorAsync(_columnAccess, cancellationToken), cancellationToken);

        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    /// <summary>Справочники одним запросом — для заполнения выпадающих списков.</summary>
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup(CancellationToken cancellationToken)
    {
        return Ok(new
        {
            typesOfPurchase = await _dictionaries.GetTypesOfPurchaseAsync(cancellationToken),
            b2bStatuses = await _dictionaries.GetB2BStatusesAsync(cancellationToken),
            executionStatuses = await _dictionaries.GetExecutionStatusesAsync(cancellationToken)
        });
    }

    private string CurrentUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "-";

    private ActionResult<RegistryRowDto> ToActionResult(Result<RegistryRowDto> result, int successStatus)
    {
        if (result.Success && result.Value is not null)
        {
            return StatusCode(successStatus, result.Value);
        }

        return BadRequest(new { error = result.Error, fieldErrors = result.FieldErrors });
    }
}

/// <summary>Тело запроса на подсветку ячейки.</summary>
public sealed class CellColorRequest
{
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>null или пустая строка — снять подсветку.</summary>
    public string? ColorCode { get; set; }
}
