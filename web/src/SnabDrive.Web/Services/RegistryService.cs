using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;
using SnabDrive.Web.Services.Validation;

namespace SnabDrive.Web.Services;

public interface IRegistryService
{
    Task<PagedResult<RegistryRowDto>> QueryAsync(RegistryQuery query, ColumnAccessMap? access = null, CancellationToken cancellationToken = default);

    Task<PagedResult<RegistryRowDto>> QueryArchiveAsync(RegistryQuery query, ColumnAccessMap? access = null, CancellationToken cancellationToken = default);

    Task<RegistryRowDto?> GetByIdAsync(int id, ColumnAccessMap? access = null, CancellationToken cancellationToken = default);

    Task<RegeditUpsertRequest?> GetEditModelAsync(int id, ColumnAccessMap? access = null, CancellationToken cancellationToken = default);

    Task<Result<RegistryRowDto>> CreateAsync(RegeditUpsertRequest request, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<Result<RegistryRowDto>> UpdateAsync(int id, RegeditUpsertRequest request, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<Result> ArchiveAsync(int id, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<Result> RestoreAsync(int archiveId, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<Result> UpdateArchiveAsync(int archiveId, RegeditUpsertRequest request, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<Result> DeleteArchiveAsync(int archiveId, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetCellColorsAsync(IEnumerable<int> recordIds, CancellationToken cancellationToken = default);

    Task<Result> SetCellColorAsync(int recordId, string columnName, string? colorCode, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<int> CountArchiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Бизнес-логика реестра: выборка с поиском/сортировкой/фильтрами/пагинацией, CRUD,
/// архивация, подсветка ячеек. Один и тот же код используют и Web API, и Blazor UI.
/// </summary>
public sealed class RegistryService : IRegistryService
{
    public const string EntityName = "Regedit";
    public const string ArchiveEntityName = "ArchiveRegedit";

    private readonly IDbContextFactory<RegistryDbContext> _factory;
    private readonly IAuditService _audit;
    private readonly IRegistryNotifier _notifier;
    private readonly ILogger<RegistryService> _logger;

    public RegistryService(
        IDbContextFactory<RegistryDbContext> factory,
        IAuditService audit,
        IRegistryNotifier notifier,
        ILogger<RegistryService> logger)
    {
        _factory = factory;
        _audit = audit;
        _notifier = notifier;
        _logger = logger;
    }

    // ------------------------------------------------------------------ выборка

    public async Task<PagedResult<RegistryRowDto>> QueryAsync(RegistryQuery query, ColumnAccessMap? access = null, CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize();
        var map = access ?? ColumnAccessMap.Full;

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var filtered = ApplyColumnFilters(context.Regedit.AsNoTracking(), normalized);
        filtered = ApplySearch(filtered, normalized.Search);

        var total = await filtered.CountAsync(cancellationToken);
        var sorted = ApplySort(filtered, normalized.SortBy, normalized.SortDescending);

        var items = await sorted
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .Select(x => new RegistryRowDto
            {
                Id = x.Id,
                IdOld = null,
                NameLink = x.NameLink,
                Customer = x.Customer,
                TypeOfPurchaseId = x.TypeOfPurchaseId,
                TypeOfPurchaseName = x.TypeOfPurchase != null ? x.TypeOfPurchase.NameOfPurchase : null,
                TypeOfPurchaseColor = x.TypeOfPurchase != null ? x.TypeOfPurchase.ColorCode : null,
                PlaceOfDelivery = x.PlaceOfDelivery,
                ReserveNumber = x.ReserveNumber,
                NationalMode = x.NationalMode,
                BiddingDate = x.BiddingDate,
                DateOfTransferForPlacement = x.DateOfTransferForPlacement,
                DateOfPlacement = x.DateOfPlacement,
                DateResults = x.DateResults,
                DateOfConclusionOfTheContract = x.DateOfConclusionOfTheContract,
                NMCK = x.NMCK,
                MinPrice = x.MinPrice,
                ResultPrice = x.ResultPrice,
                Winner = x.Winner,
                DeliveryTime = x.DeliveryTime,
                Description = x.Description,
                Note = x.Note,
                B2BStatusId = x.B2BStatusId,
                B2BStatusName = x.B2BStatus != null ? x.B2BStatus.NameB2B : null,
                ExecutionStatusId = x.ExecutionStatusId,
                ExecutionStatusName = x.ExecutionStatus != null ? x.ExecutionStatus.NameExecution : null,
                IsFinished = x.IsFinished,
                ArchivateDate = null
            })
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            MaskRow(item, map);
        }

        return new PagedResult<RegistryRowDto>
        {
            Items = items,
            TotalCount = total,
            Page = normalized.Page,
            PageSize = normalized.PageSize
        };
    }

    public async Task<PagedResult<RegistryRowDto>> QueryArchiveAsync(RegistryQuery query, ColumnAccessMap? access = null, CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize();
        var map = access ?? ColumnAccessMap.Full;

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var filtered = ApplyArchiveColumnFilters(context.ArchiveRegedit.AsNoTracking(), normalized);
        filtered = ApplyArchiveSearch(filtered, normalized.Search);

        var total = await filtered.CountAsync(cancellationToken);
        var sorted = ApplyArchiveSort(filtered, normalized.SortBy, normalized.SortDescending);

        var items = await sorted
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .Select(x => new RegistryRowDto
            {
                Id = x.Id,
                IdOld = x.IdOld,
                NameLink = x.NameLink,
                Customer = x.Customer,
                TypeOfPurchaseId = x.TypeOfPurchaseId,
                TypeOfPurchaseName = x.TypeOfPurchase != null ? x.TypeOfPurchase.NameOfPurchase : null,
                TypeOfPurchaseColor = x.TypeOfPurchase != null ? x.TypeOfPurchase.ColorCode : null,
                PlaceOfDelivery = x.PlaceOfDelivery,
                ReserveNumber = x.ReserveNumber,
                NationalMode = x.NationalMode,
                BiddingDate = x.BiddingDate,
                DateOfTransferForPlacement = x.DateOfTransferForPlacement,
                DateOfPlacement = x.DateOfPlacement,
                DateResults = x.DateResults,
                DateOfConclusionOfTheContract = x.DateOfConclusionOfTheContract,
                NMCK = x.NMCK,
                MinPrice = x.MinPrice,
                ResultPrice = x.ResultPrice,
                Winner = x.Winner,
                DeliveryTime = x.DeliveryTime,
                Description = x.Description,
                Note = x.Note,
                B2BStatusId = x.B2BStatusId,
                B2BStatusName = x.B2BStatus != null ? x.B2BStatus.NameB2B : null,
                ExecutionStatusId = x.ExecutionStatusId,
                ExecutionStatusName = x.ExecutionStatus != null ? x.ExecutionStatus.NameExecution : null,
                IsFinished = null,
                ArchivateDate = x.ArchivateDate
            })
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            MaskRow(item, map);
        }

        return new PagedResult<RegistryRowDto>
        {
            Items = items,
            TotalCount = total,
            Page = normalized.Page,
            PageSize = normalized.PageSize
        };
    }

    public async Task<RegistryRowDto?> GetByIdAsync(int id, ColumnAccessMap? access = null, CancellationToken cancellationToken = default)
    {
        var row = await LoadRowAsync(id, cancellationToken);
        if (row is not null)
        {
            MaskRow(row, access ?? ColumnAccessMap.Full);
        }

        return row;
    }

    public async Task<RegeditUpsertRequest?> GetEditModelAsync(int id, ColumnAccessMap? access = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Regedit.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var request = ToRequest(entity);
        MaskRequest(request, access ?? ColumnAccessMap.Full);
        return request;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.Regedit.CountAsync(cancellationToken);
    }

    public async Task<int> CountArchiveAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.ArchiveRegedit.CountAsync(cancellationToken);
    }

    private async Task<RegistryRowDto?> LoadRowAsync(int id, CancellationToken cancellationToken)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        return await context.Regedit.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new RegistryRowDto
            {
                Id = x.Id,
                IdOld = null,
                NameLink = x.NameLink,
                Customer = x.Customer,
                TypeOfPurchaseId = x.TypeOfPurchaseId,
                TypeOfPurchaseName = x.TypeOfPurchase != null ? x.TypeOfPurchase.NameOfPurchase : null,
                TypeOfPurchaseColor = x.TypeOfPurchase != null ? x.TypeOfPurchase.ColorCode : null,
                PlaceOfDelivery = x.PlaceOfDelivery,
                ReserveNumber = x.ReserveNumber,
                NationalMode = x.NationalMode,
                BiddingDate = x.BiddingDate,
                DateOfTransferForPlacement = x.DateOfTransferForPlacement,
                DateOfPlacement = x.DateOfPlacement,
                DateResults = x.DateResults,
                DateOfConclusionOfTheContract = x.DateOfConclusionOfTheContract,
                NMCK = x.NMCK,
                MinPrice = x.MinPrice,
                ResultPrice = x.ResultPrice,
                Winner = x.Winner,
                DeliveryTime = x.DeliveryTime,
                Description = x.Description,
                Note = x.Note,
                B2BStatusId = x.B2BStatusId,
                B2BStatusName = x.B2BStatus != null ? x.B2BStatus.NameB2B : null,
                ExecutionStatusId = x.ExecutionStatusId,
                ExecutionStatusName = x.ExecutionStatus != null ? x.ExecutionStatus.NameExecution : null,
                IsFinished = x.IsFinished,
                ArchivateDate = null
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    // ------------------------------------------------------------------ CRUD

    public async Task<Result<RegistryRowDto>> CreateAsync(RegeditUpsertRequest request, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        // При создании нельзя «подставить» значение из базы, как при редактировании, —
        // обязательные поля без права на правку остались бы пустыми.
        if (!actor.Access.CanEdit("NameLink") || !actor.Access.CanEdit("Customer"))
        {
            return Result<RegistryRowDto>.Fail(
                "Для создания записи нужны права на редактирование полей «Ссылка / наименование закупки» и «Заказчик»");
        }

        var errors = RegistryValidator.Validate(request, actor.Access);
        if (errors.Count > 0)
        {
            return Result<RegistryRowDto>.Invalid(errors);
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var dictError = await ValidateDictionariesAsync(context, request, cancellationToken);
        if (dictError is not null)
        {
            return Result<RegistryRowDto>.Fail(dictError);
        }

        var entity = new Regedit();
        Apply(entity, request, actor.Access);

        context.Regedit.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        var row = await LoadRowAsync(entity.Id, cancellationToken) ?? new RegistryRowDto { Id = entity.Id };

        await _audit.WriteAsync(AuditAction.Created, EntityName, entity.Id, actor,
            BuildChanges(null, request), $"Добавлена запись «{Short(entity.NameLink)}»", cancellationToken);

        await _notifier.PublishAsync(new RegistryChangeEvent(
            ChangeKind.Created, EntityName, entity.Id, actor.UserName, DateTime.UtcNow,
            $"{actor.UserName} добавил(а) запись «{Short(entity.NameLink)}»", row), cancellationToken);

        return Result<RegistryRowDto>.Ok(row);
    }

    public async Task<Result<RegistryRowDto>> UpdateAsync(int id, RegeditUpsertRequest request, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        var errors = RegistryValidator.Validate(request, actor.Access);
        if (errors.Count > 0)
        {
            return Result<RegistryRowDto>.Invalid(errors);
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Regedit.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result<RegistryRowDto>.Fail($"Запись #{id} не найдена. Возможно, её уже удалили.");
        }

        var dictError = await ValidateDictionariesAsync(context, request, cancellationToken);
        if (dictError is not null)
        {
            return Result<RegistryRowDto>.Fail(dictError);
        }

        var before = ToRequest(entity);
        Apply(entity, request, actor.Access);
        await context.SaveChangesAsync(cancellationToken);

        var row = await LoadRowAsync(id, cancellationToken) ?? new RegistryRowDto { Id = id };
        var changes = BuildChanges(before, request);

        if (changes.Count > 0)
        {
            await _audit.WriteAsync(AuditAction.Updated, EntityName, id, actor, changes,
                $"Изменена запись «{Short(entity.NameLink)}»", cancellationToken);

            await _notifier.PublishAsync(new RegistryChangeEvent(
                ChangeKind.Updated, EntityName, id, actor.UserName, DateTime.UtcNow,
                $"{actor.UserName} изменил(а) запись «{Short(entity.NameLink)}»", row), cancellationToken);
        }

        return Result<RegistryRowDto>.Ok(row);
    }

    public async Task<Result> DeleteAsync(int id, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Regedit.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result.Fail($"Запись #{id} не найдена.");
        }

        var name = entity.NameLink;
        context.Regedit.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(AuditAction.Deleted, EntityName, id, actor,
            summary: $"Удалена запись «{Short(name)}»", cancellationToken: cancellationToken);

        await _notifier.PublishAsync(new RegistryChangeEvent(
            ChangeKind.Deleted, EntityName, id, actor.UserName, DateTime.UtcNow,
            $"{actor.UserName} удалил(а) запись «{Short(name)}»"), cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> ArchiveAsync(int id, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Regedit.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result.Fail($"Запись #{id} не найдена.");
        }

        var archive = new ArchiveRegedit
        {
            IdOld = entity.Id,
            NameLink = entity.NameLink,
            PlaceOfDelivery = entity.PlaceOfDelivery,
            ReserveNumber = entity.ReserveNumber,
            NationalMode = entity.NationalMode,
            DateOfTransferForPlacement = entity.DateOfTransferForPlacement,
            DateOfPlacement = entity.DateOfPlacement,
            BiddingDate = entity.BiddingDate,
            DateResults = entity.DateResults,
            NMCK = entity.NMCK,
            MinPrice = entity.MinPrice,
            ResultPrice = entity.ResultPrice,
            Winner = entity.Winner,
            DateOfConclusionOfTheContract = entity.DateOfConclusionOfTheContract,
            DeliveryTime = entity.DeliveryTime,
            Description = entity.Description,
            Note = entity.Note,
            Customer = entity.Customer,
            TypeOfPurchaseId = entity.TypeOfPurchaseId,
            B2BStatusId = entity.B2BStatusId,
            ExecutionStatusId = entity.ExecutionStatusId,
            ArchivateDate = DateTime.Now
        };

        context.ArchiveRegedit.Add(archive);
        context.Regedit.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(AuditAction.Archived, EntityName, id, actor,
            summary: $"Запись «{Short(archive.NameLink)}» перемещена в архив", cancellationToken: cancellationToken);

        await _notifier.PublishAsync(new RegistryChangeEvent(
            ChangeKind.Archived, EntityName, id, actor.UserName, DateTime.UtcNow,
            $"{actor.UserName} архивировал(а) запись «{Short(archive.NameLink)}»"), cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> RestoreAsync(int archiveId, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var archive = await context.ArchiveRegedit.FirstOrDefaultAsync(x => x.Id == archiveId, cancellationToken);
        if (archive is null)
        {
            return Result.Fail($"Архивная запись #{archiveId} не найдена.");
        }

        var restored = new Regedit
        {
            NameLink = archive.NameLink,
            PlaceOfDelivery = archive.PlaceOfDelivery,
            ReserveNumber = archive.ReserveNumber,
            NationalMode = archive.NationalMode,
            DateOfTransferForPlacement = archive.DateOfTransferForPlacement,
            DateOfPlacement = archive.DateOfPlacement,
            BiddingDate = archive.BiddingDate,
            DateResults = archive.DateResults,
            NMCK = archive.NMCK,
            MinPrice = archive.MinPrice,
            ResultPrice = archive.ResultPrice,
            Winner = archive.Winner,
            DateOfConclusionOfTheContract = archive.DateOfConclusionOfTheContract,
            DeliveryTime = archive.DeliveryTime,
            Description = archive.Description,
            Note = archive.Note,
            Customer = archive.Customer,
            TypeOfPurchaseId = archive.TypeOfPurchaseId,
            B2BStatusId = archive.B2BStatusId,
            ExecutionStatusId = archive.ExecutionStatusId,
            IsFinished = false
        };

        context.Regedit.Add(restored);
        context.ArchiveRegedit.Remove(archive);
        await context.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(AuditAction.Restored, EntityName, restored.Id, actor,
            summary: $"Запись «{Short(restored.NameLink)}» возвращена из архива (была #{archive.IdOld})",
            cancellationToken: cancellationToken);

        await _notifier.PublishAsync(new RegistryChangeEvent(
            ChangeKind.Restored, EntityName, restored.Id, actor.UserName, DateTime.UtcNow,
            $"{actor.UserName} вернул(а) из архива запись «{Short(restored.NameLink)}»"), cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> UpdateArchiveAsync(int archiveId, RegeditUpsertRequest request, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        var errors = RegistryValidator.Validate(request, actor.Access);
        if (errors.Count > 0)
        {
            return Result.Invalid(errors);
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = await context.ArchiveRegedit.FirstOrDefaultAsync(x => x.Id == archiveId, cancellationToken);
        if (entity is null)
        {
            return Result.Fail($"Архивная запись #{archiveId} не найдена.");
        }

        var before = ToRequestFromArchive(entity);

        entity.NameLink = request.NameLink;
        entity.Customer = request.Customer;
        entity.PlaceOfDelivery = request.PlaceOfDelivery;
        entity.ReserveNumber = request.ReserveNumber;
        entity.NationalMode = request.NationalMode;
        entity.DateOfTransferForPlacement = request.DateOfTransferForPlacement;
        entity.DateOfPlacement = request.DateOfPlacement;
        entity.BiddingDate = request.BiddingDate;
        entity.DateResults = request.DateResults;
        entity.DateOfConclusionOfTheContract = request.DateOfConclusionOfTheContract;
        entity.NMCK = request.NMCK;
        entity.MinPrice = request.MinPrice;
        entity.ResultPrice = request.ResultPrice;
        entity.Winner = request.Winner;
        entity.DeliveryTime = request.DeliveryTime;
        entity.Description = request.Description;
        entity.Note = request.Note;
        entity.TypeOfPurchaseId = Normalize(request.TypeOfPurchaseId);
        entity.B2BStatusId = Normalize(request.B2BStatusId);
        entity.ExecutionStatusId = Normalize(request.ExecutionStatusId);

        await context.SaveChangesAsync(cancellationToken);

        var changes = BuildChanges(before, request);
        if (changes.Count > 0)
        {
            await _audit.WriteAsync(AuditAction.Updated, ArchiveEntityName, archiveId, actor, changes,
                $"Изменена архивная запись «{Short(entity.NameLink)}»", cancellationToken);

            await _notifier.PublishAsync(new RegistryChangeEvent(
                ChangeKind.Updated, ArchiveEntityName, archiveId, actor.UserName, DateTime.UtcNow,
                $"{actor.UserName} изменил(а) архивную запись «{Short(entity.NameLink)}»"), cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<Result> DeleteArchiveAsync(int archiveId, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = await context.ArchiveRegedit.FirstOrDefaultAsync(x => x.Id == archiveId, cancellationToken);
        if (entity is null)
        {
            return Result.Fail($"Архивная запись #{archiveId} не найдена.");
        }

        var name = entity.NameLink;
        context.ArchiveRegedit.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(AuditAction.Deleted, ArchiveEntityName, archiveId, actor,
            summary: $"Удалена архивная запись «{Short(name)}»", cancellationToken: cancellationToken);

        await _notifier.PublishAsync(new RegistryChangeEvent(
            ChangeKind.Deleted, ArchiveEntityName, archiveId, actor.UserName, DateTime.UtcNow,
            $"{actor.UserName} удалил(а) архивную запись «{Short(name)}»"), cancellationToken);

        return Result.Ok();
    }

    // ------------------------------------------------------------------ цвета ячеек

    public async Task<IReadOnlyDictionary<string, string>> GetCellColorsAsync(IEnumerable<int> recordIds, CancellationToken cancellationToken = default)
    {
        var ids = recordIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var rows = await context.CellColors.AsNoTracking()
            .Where(c => ids.Contains(c.RegeditId))
            .Select(c => new { c.RegeditId, c.ColumnName, c.ColorCode })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            map[CellColorKey(row.RegeditId, row.ColumnName)] = row.ColorCode;
        }

        return map;
    }

    public async Task<Result> SetCellColorAsync(int recordId, string columnName, string? colorCode, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (!RegistryColumns.IsKnown(columnName))
        {
            return Result.Fail($"Неизвестная колонка «{columnName}».");
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var existing = await context.CellColors
            .FirstOrDefaultAsync(c => c.RegeditId == recordId && c.ColumnName == columnName, cancellationToken);

        var previousColor = existing?.ColorCode;

        if (string.IsNullOrWhiteSpace(colorCode))
        {
            if (existing is not null)
            {
                context.CellColors.Remove(existing);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        else if (existing is not null)
        {
            existing.ColorCode = colorCode;
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            context.CellColors.Add(new CellColor
            {
                RegeditId = recordId,
                ColumnName = columnName,
                ColorCode = colorCode
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        await _audit.WriteAsync(AuditAction.CellColorChanged, EntityName, recordId, actor,
            new Dictionary<string, FieldChange>
            {
                [columnName] = new(previousColor, string.IsNullOrWhiteSpace(colorCode) ? null : colorCode)
            },
            $"Изменена подсветка колонки «{RegistryColumns.Find(columnName)?.Title ?? columnName}»", cancellationToken);

        await _notifier.PublishAsync(new RegistryChangeEvent(
            ChangeKind.CellColorChanged, EntityName, recordId, actor.UserName, DateTime.UtcNow,
            $"{actor.UserName} изменил(а) подсветку ячейки", null), cancellationToken);

        return Result.Ok();
    }

    // ------------------------------------------------------------------ helpers

    // ------------------------------------------------------------------ права на колонки

    /// <summary>
    /// Обнуляет значения колонок, которые пользователю смотреть нельзя.
    /// Делается на сервере, а не только в UI, — иначе значения уехали бы в браузер в разметке.
    /// </summary>
    internal static void MaskRow(RegistryRowDto row, ColumnAccessMap access)
    {
        if (access.Unrestricted)
        {
            return;
        }

        if (!access.CanView("NameLink")) row.NameLink = string.Empty;
        if (!access.CanView("Customer")) row.Customer = string.Empty;
        if (!access.CanView("TypeOfPurchaseId"))
        {
            row.TypeOfPurchaseId = null;
            row.TypeOfPurchaseName = null;
            row.TypeOfPurchaseColor = null;
        }

        if (!access.CanView("PlaceOfDelivery")) row.PlaceOfDelivery = string.Empty;
        if (!access.CanView("ReserveNumber")) row.ReserveNumber = string.Empty;
        if (!access.CanView("NationalMode")) row.NationalMode = string.Empty;
        if (!access.CanView("BiddingDate")) row.BiddingDate = null;
        if (!access.CanView("DateOfTransferForPlacement")) row.DateOfTransferForPlacement = null;
        if (!access.CanView("DateOfPlacement")) row.DateOfPlacement = null;
        if (!access.CanView("DateResults")) row.DateResults = null;
        if (!access.CanView("DateOfConclusionOfTheContract")) row.DateOfConclusionOfTheContract = null;
        if (!access.CanView("NMCK")) row.NMCK = 0m;
        if (!access.CanView("MinPrice")) row.MinPrice = 0m;
        if (!access.CanView("ResultPrice")) row.ResultPrice = 0m;
        if (!access.CanView("Winner")) row.Winner = string.Empty;
        if (!access.CanView("DeliveryTime")) row.DeliveryTime = string.Empty;
        if (!access.CanView("Description")) row.Description = string.Empty;
        if (!access.CanView("Note")) row.Note = string.Empty;

        if (!access.CanView("B2BStatusId"))
        {
            row.B2BStatusId = null;
            row.B2BStatusName = null;
        }

        if (!access.CanView("ExecutionStatusId"))
        {
            row.ExecutionStatusId = null;
            row.ExecutionStatusName = null;
        }

        if (!access.CanView("IsFinished")) row.IsFinished = null;
        if (!access.CanView("ArchivateDate")) row.ArchivateDate = null;
    }

    /// <summary>То же самое для формы редактирования — скрытые поля не должны попадать в браузер.</summary>
    internal static void MaskRequest(RegeditUpsertRequest request, ColumnAccessMap access)
    {
        if (access.Unrestricted)
        {
            return;
        }

        if (!access.CanView("NameLink")) request.NameLink = string.Empty;
        if (!access.CanView("Customer")) request.Customer = string.Empty;
        if (!access.CanView("PlaceOfDelivery")) request.PlaceOfDelivery = string.Empty;
        if (!access.CanView("ReserveNumber")) request.ReserveNumber = string.Empty;
        if (!access.CanView("NationalMode")) request.NationalMode = string.Empty;
        if (!access.CanView("DateOfTransferForPlacement")) request.DateOfTransferForPlacement = null;
        if (!access.CanView("DateOfPlacement")) request.DateOfPlacement = null;
        if (!access.CanView("BiddingDate")) request.BiddingDate = null;
        if (!access.CanView("DateResults")) request.DateResults = null;
        if (!access.CanView("DateOfConclusionOfTheContract")) request.DateOfConclusionOfTheContract = null;
        if (!access.CanView("NMCK")) request.NMCK = 0m;
        if (!access.CanView("MinPrice")) request.MinPrice = 0m;
        if (!access.CanView("ResultPrice")) request.ResultPrice = 0m;
        if (!access.CanView("Winner")) request.Winner = string.Empty;
        if (!access.CanView("DeliveryTime")) request.DeliveryTime = string.Empty;
        if (!access.CanView("Description")) request.Description = string.Empty;
        if (!access.CanView("Note")) request.Note = string.Empty;
        if (!access.CanView("TypeOfPurchaseId")) request.TypeOfPurchaseId = null;
        if (!access.CanView("B2BStatusId")) request.B2BStatusId = null;
        if (!access.CanView("ExecutionStatusId")) request.ExecutionStatusId = null;
        if (!access.CanView("IsFinished")) request.IsFinished = false;
    }

    public static string CellColorKey(int recordId, string columnName) => $"{recordId}|{columnName}";

    private static string Short(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= 60 ? value : value[..57] + "...");

    private static async Task<string?> ValidateDictionariesAsync(
        RegistryDbContext context, RegeditUpsertRequest request, CancellationToken cancellationToken)
    {
        var typeOfPurchaseId = request.TypeOfPurchaseId;
        if (typeOfPurchaseId is > 0 && !await context.TypesOfPurchase.AnyAsync(x => x.Id == typeOfPurchaseId, cancellationToken))
        {
            return "Выбранный тип закупки больше не существует. Обновите страницу.";
        }

        var b2bStatusId = request.B2BStatusId;
        if (b2bStatusId is > 0 && !await context.B2BStatuses.AnyAsync(x => x.Id == b2bStatusId, cancellationToken))
        {
            return "Выбранный статус B2B больше не существует. Обновите страницу.";
        }

        var executionStatusId = request.ExecutionStatusId;
        if (executionStatusId is > 0 && !await context.ExecutionStatuses.AnyAsync(x => x.Id == executionStatusId, cancellationToken))
        {
            return "Выбранный статус исполнения больше не существует. Обновите страницу.";
        }

        return null;
    }

    /// <summary>
    /// Переносит значения формы в сущность, но только по тем полям, которые пользователю
    /// разрешено редактировать. Остальные поля сохраняют текущее значение в базе.
    /// </summary>
    private static void Apply(Regedit entity, RegeditUpsertRequest request, ColumnAccessMap access)
    {
        if (access.CanEdit("NameLink")) entity.NameLink = request.NameLink.Trim();
        if (access.CanEdit("Customer")) entity.Customer = request.Customer.Trim();
        if (access.CanEdit("PlaceOfDelivery")) entity.PlaceOfDelivery = request.PlaceOfDelivery;
        if (access.CanEdit("ReserveNumber")) entity.ReserveNumber = request.ReserveNumber;
        if (access.CanEdit("NationalMode")) entity.NationalMode = request.NationalMode;
        if (access.CanEdit("DateOfTransferForPlacement")) entity.DateOfTransferForPlacement = request.DateOfTransferForPlacement;
        if (access.CanEdit("DateOfPlacement")) entity.DateOfPlacement = request.DateOfPlacement;
        if (access.CanEdit("BiddingDate")) entity.BiddingDate = request.BiddingDate;
        if (access.CanEdit("DateResults")) entity.DateResults = request.DateResults;
        if (access.CanEdit("DateOfConclusionOfTheContract")) entity.DateOfConclusionOfTheContract = request.DateOfConclusionOfTheContract;
        if (access.CanEdit("NMCK")) entity.NMCK = request.NMCK;
        if (access.CanEdit("MinPrice")) entity.MinPrice = request.MinPrice;
        if (access.CanEdit("ResultPrice")) entity.ResultPrice = request.ResultPrice;
        if (access.CanEdit("Winner")) entity.Winner = request.Winner;
        if (access.CanEdit("DeliveryTime")) entity.DeliveryTime = request.DeliveryTime;
        if (access.CanEdit("Description")) entity.Description = request.Description;
        if (access.CanEdit("Note")) entity.Note = request.Note;
        if (access.CanEdit("TypeOfPurchaseId")) entity.TypeOfPurchaseId = Normalize(request.TypeOfPurchaseId);
        if (access.CanEdit("B2BStatusId")) entity.B2BStatusId = Normalize(request.B2BStatusId);
        if (access.CanEdit("ExecutionStatusId")) entity.ExecutionStatusId = Normalize(request.ExecutionStatusId);
        if (access.CanEdit("IsFinished")) entity.IsFinished = request.IsFinished;
    }

    // В WPF-версии «нет значения» хранилось как 0 — сохраняем то же поведение, чтобы не ломать данные.
    private static int? Normalize(int? value) => value is > 0 ? value : 0;

    private static RegeditUpsertRequest ToRequest(Regedit entity) => new()
    {
        NameLink = entity.NameLink,
        Customer = entity.Customer,
        PlaceOfDelivery = entity.PlaceOfDelivery,
        ReserveNumber = entity.ReserveNumber,
        NationalMode = entity.NationalMode,
        DateOfTransferForPlacement = entity.DateOfTransferForPlacement,
        DateOfPlacement = entity.DateOfPlacement,
        BiddingDate = entity.BiddingDate,
        DateResults = entity.DateResults,
        DateOfConclusionOfTheContract = entity.DateOfConclusionOfTheContract,
        NMCK = entity.NMCK,
        MinPrice = entity.MinPrice,
        ResultPrice = entity.ResultPrice,
        Winner = entity.Winner,
        DeliveryTime = entity.DeliveryTime,
        Description = entity.Description,
        Note = entity.Note,
        TypeOfPurchaseId = entity.TypeOfPurchaseId,
        B2BStatusId = entity.B2BStatusId,
        ExecutionStatusId = entity.ExecutionStatusId,
        IsFinished = entity.IsFinished ?? false
    };

    private static RegeditUpsertRequest ToRequestFromArchive(ArchiveRegedit entity) => new()
    {
        NameLink = entity.NameLink,
        Customer = entity.Customer,
        PlaceOfDelivery = entity.PlaceOfDelivery,
        ReserveNumber = entity.ReserveNumber,
        NationalMode = entity.NationalMode,
        DateOfTransferForPlacement = entity.DateOfTransferForPlacement,
        DateOfPlacement = entity.DateOfPlacement,
        BiddingDate = entity.BiddingDate,
        DateResults = entity.DateResults,
        DateOfConclusionOfTheContract = entity.DateOfConclusionOfTheContract,
        NMCK = entity.NMCK,
        MinPrice = entity.MinPrice,
        ResultPrice = entity.ResultPrice,
        Winner = entity.Winner,
        DeliveryTime = entity.DeliveryTime,
        Description = entity.Description,
        Note = entity.Note,
        TypeOfPurchaseId = entity.TypeOfPurchaseId,
        B2BStatusId = entity.B2BStatusId,
        ExecutionStatusId = entity.ExecutionStatusId
    };

    internal static Dictionary<string, FieldChange> BuildChanges(RegeditUpsertRequest? before, RegeditUpsertRequest after)
    {
        var changes = new Dictionary<string, FieldChange>(StringComparer.Ordinal);

        if (before is null)
        {
            changes["Ссылка / наименование"] = new(null, after.NameLink);
            changes["Заказчик"] = new(null, after.Customer);
            return changes;
        }

        Compare("Ссылка / наименование", before.NameLink, after.NameLink);
        Compare("Заказчик", before.Customer, after.Customer);
        Compare("Место поставки", before.PlaceOfDelivery, after.PlaceOfDelivery);
        Compare("Номер резерва", before.ReserveNumber, after.ReserveNumber);
        Compare("Нац режим", before.NationalMode, after.NationalMode);
        Compare("Дата торгов", Fmt(before.BiddingDate), Fmt(after.BiddingDate));
        Compare("Дата передачи на размещение", Fmt(before.DateOfTransferForPlacement), Fmt(after.DateOfTransferForPlacement));
        Compare("Дата размещения", Fmt(before.DateOfPlacement), Fmt(after.DateOfPlacement));
        Compare("Дата подведения итогов", Fmt(before.DateResults), Fmt(after.DateResults));
        Compare("Дата заключения контракта", Fmt(before.DateOfConclusionOfTheContract), Fmt(after.DateOfConclusionOfTheContract));
        Compare("НМЦК", Money(before.NMCK), Money(after.NMCK));
        Compare("Наша минимальная сумма", Money(before.MinPrice), Money(after.MinPrice));
        Compare("Итоговая сумма", Money(before.ResultPrice), Money(after.ResultPrice));
        Compare("Победитель", before.Winner, after.Winner);
        Compare("Срок поставки", before.DeliveryTime, after.DeliveryTime);
        Compare("Примечания", before.Description, after.Description);
        Compare("Доп записка", before.Note, after.Note);
        Compare("Тип закупки", before.TypeOfPurchaseId?.ToString(), after.TypeOfPurchaseId?.ToString());
        Compare("Статус B2B", before.B2BStatusId?.ToString(), after.B2BStatusId?.ToString());
        Compare("Статус исполнения", before.ExecutionStatusId?.ToString(), after.ExecutionStatusId?.ToString());
        Compare("Оплачен", before.IsFinished ? "да" : "нет", after.IsFinished ? "да" : "нет");

        return changes;

        void Compare(string title, string? oldValue, string? newValue)
        {
            if (!string.Equals(oldValue ?? string.Empty, newValue ?? string.Empty, StringComparison.Ordinal))
            {
                changes[title] = new(oldValue, newValue);
            }
        }

        static string? Fmt(DateTime? value) => value?.ToString("dd.MM.yyyy");
        static string Money(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------ поиск

    /// <summary>
    /// Глобальный поиск — аналог IsMatch() из WPF: текст по всем строковым полям и названиям
    /// справочников, плюс точное совпадение сумм, конкретной даты или года.
    /// </summary>
    private static IQueryable<Regedit> ApplySearch(IQueryable<Regedit> source, string? search)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return source;
        }

        // Ищем без учёта регистра (как в WPF-версии) — иначе результат зависел бы от collation базы.
        var like = $"%{EscapeLike(term.ToLowerInvariant())}%";
        var hasAmount = decimal.TryParse(term, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount);
        var isYearOnly = term.Length == 4 && int.TryParse(term, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        DateTime date = default;
        var hasDate = !isYearOnly && TryParseDate(term, out date);
        var hasYear = isYearOnly;
        var yearValue = hasYear ? int.Parse(term, CultureInfo.InvariantCulture) : 0;

        // Сравниваем диапазоном, а не .Date/.Year: так запрос одинаково транслируется
        // и в SQL Server, и в SQLite.
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);
        var yearStart = new DateTime(yearValue == 0 ? 1 : yearValue, 1, 1);
        var yearEnd = yearStart.AddYears(1);

        return source.Where(x =>
            EF.Functions.Like(x.NameLink.ToLower(), like) ||
            EF.Functions.Like(x.Customer.ToLower(), like) ||
            EF.Functions.Like(x.PlaceOfDelivery.ToLower(), like) ||
            EF.Functions.Like(x.ReserveNumber.ToLower(), like) ||
            EF.Functions.Like(x.NationalMode.ToLower(), like) ||
            EF.Functions.Like(x.Winner.ToLower(), like) ||
            EF.Functions.Like(x.Description.ToLower(), like) ||
            EF.Functions.Like(x.Note.ToLower(), like) ||
            (x.TypeOfPurchase != null && EF.Functions.Like(x.TypeOfPurchase.NameOfPurchase.ToLower(), like)) ||
            (x.B2BStatus != null && EF.Functions.Like(x.B2BStatus.NameB2B.ToLower(), like)) ||
            (x.ExecutionStatus != null && EF.Functions.Like(x.ExecutionStatus.NameExecution.ToLower(), like)) ||
            (hasAmount && (x.NMCK == amount || x.MinPrice == amount || x.ResultPrice == amount)) ||
            (hasDate && ((x.BiddingDate >= dayStart && x.BiddingDate < dayEnd) ||
                         (x.DateOfPlacement >= dayStart && x.DateOfPlacement < dayEnd) ||
                         (x.DateOfTransferForPlacement >= dayStart && x.DateOfTransferForPlacement < dayEnd) ||
                         (x.DateResults >= dayStart && x.DateResults < dayEnd) ||
                         (x.DateOfConclusionOfTheContract >= dayStart && x.DateOfConclusionOfTheContract < dayEnd))) ||
            (hasYear && ((x.BiddingDate >= yearStart && x.BiddingDate < yearEnd) ||
                         (x.DateOfPlacement >= yearStart && x.DateOfPlacement < yearEnd) ||
                         (x.DateOfTransferForPlacement >= yearStart && x.DateOfTransferForPlacement < yearEnd) ||
                         (x.DateResults >= yearStart && x.DateResults < yearEnd) ||
                         (x.DateOfConclusionOfTheContract >= yearStart && x.DateOfConclusionOfTheContract < yearEnd))));
    }

    private static IQueryable<ArchiveRegedit> ApplyArchiveSearch(IQueryable<ArchiveRegedit> source, string? search)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return source;
        }

        // Ищем без учёта регистра (как в WPF-версии) — иначе результат зависел бы от collation базы.
        var like = $"%{EscapeLike(term.ToLowerInvariant())}%";
        var hasAmount = decimal.TryParse(term, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount);
        var isYearOnly = term.Length == 4 && int.TryParse(term, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        DateTime date = default;
        var hasDate = !isYearOnly && TryParseDate(term, out date);
        var hasYear = isYearOnly;
        var yearValue = hasYear ? int.Parse(term, CultureInfo.InvariantCulture) : 0;

        // Сравниваем диапазоном, а не .Date/.Year: так запрос одинаково транслируется
        // и в SQL Server, и в SQLite.
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);
        var yearStart = new DateTime(yearValue == 0 ? 1 : yearValue, 1, 1);
        var yearEnd = yearStart.AddYears(1);

        return source.Where(x =>
            EF.Functions.Like(x.NameLink.ToLower(), like) ||
            EF.Functions.Like(x.Customer.ToLower(), like) ||
            EF.Functions.Like(x.PlaceOfDelivery.ToLower(), like) ||
            EF.Functions.Like(x.ReserveNumber.ToLower(), like) ||
            EF.Functions.Like(x.NationalMode.ToLower(), like) ||
            EF.Functions.Like(x.Winner.ToLower(), like) ||
            EF.Functions.Like(x.Description.ToLower(), like) ||
            EF.Functions.Like(x.Note.ToLower(), like) ||
            (x.TypeOfPurchase != null && EF.Functions.Like(x.TypeOfPurchase.NameOfPurchase.ToLower(), like)) ||
            (x.B2BStatus != null && EF.Functions.Like(x.B2BStatus.NameB2B.ToLower(), like)) ||
            (x.ExecutionStatus != null && EF.Functions.Like(x.ExecutionStatus.NameExecution.ToLower(), like)) ||
            (hasAmount && (x.NMCK == amount || x.MinPrice == amount || x.ResultPrice == amount)) ||
            (hasDate && ((x.BiddingDate >= dayStart && x.BiddingDate < dayEnd) ||
                         (x.DateOfPlacement >= dayStart && x.DateOfPlacement < dayEnd) ||
                         (x.DateOfTransferForPlacement >= dayStart && x.DateOfTransferForPlacement < dayEnd) ||
                         (x.DateResults >= dayStart && x.DateResults < dayEnd) ||
                         (x.DateOfConclusionOfTheContract >= dayStart && x.DateOfConclusionOfTheContract < dayEnd) ||
                         (x.ArchivateDate >= dayStart && x.ArchivateDate < dayEnd))) ||
            (hasYear && ((x.BiddingDate >= yearStart && x.BiddingDate < yearEnd) ||
                         (x.ArchivateDate >= yearStart && x.ArchivateDate < yearEnd))));
    }

    // ------------------------------------------------------------------ фильтры по колонкам

    private static IQueryable<Regedit> ApplyColumnFilters(IQueryable<Regedit> source, RegistryQuery query)
    {
        var q = source;

        if (query.TypeOfPurchaseId is > 0)
        {
            var typeId = query.TypeOfPurchaseId.Value;
            q = q.Where(x => x.TypeOfPurchaseId == typeId);
        }

        if (query.B2BStatusId is > 0)
        {
            var b2bId = query.B2BStatusId.Value;
            q = q.Where(x => x.B2BStatusId == b2bId);
        }

        if (query.ExecutionStatusId is > 0)
        {
            var executionId = query.ExecutionStatusId.Value;
            q = q.Where(x => x.ExecutionStatusId == executionId);
        }

        if (query.IsFinished.HasValue)
        {
            var finished = query.IsFinished.Value;
            q = q.Where(x => x.IsFinished == finished);
        }

        foreach (var pair in query.ColumnFilters)
        {
            var value = pair.Value?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            q = ApplyColumnFilter(q, pair.Key, value);
        }

        return q;
    }

    private static IQueryable<ArchiveRegedit> ApplyArchiveColumnFilters(IQueryable<ArchiveRegedit> source, RegistryQuery query)
    {
        var q = source;

        if (query.TypeOfPurchaseId is > 0)
        {
            var typeId = query.TypeOfPurchaseId.Value;
            q = q.Where(x => x.TypeOfPurchaseId == typeId);
        }

        if (query.B2BStatusId is > 0)
        {
            var b2bId = query.B2BStatusId.Value;
            q = q.Where(x => x.B2BStatusId == b2bId);
        }

        if (query.ExecutionStatusId is > 0)
        {
            var executionId = query.ExecutionStatusId.Value;
            q = q.Where(x => x.ExecutionStatusId == executionId);
        }

        foreach (var pair in query.ColumnFilters)
        {
            var value = pair.Value?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            q = ApplyArchiveColumnFilter(q, pair.Key, value);
        }

        return q;
    }

    private static IQueryable<Regedit> ApplyColumnFilter(IQueryable<Regedit> q, string key, string value)
    {
        var like = $"%{EscapeLike(value.ToLowerInvariant())}%";

        switch (key)
        {
            case "NameLink": return q.Where(x => EF.Functions.Like(x.NameLink.ToLower(), like));
            case "Customer": return q.Where(x => EF.Functions.Like(x.Customer.ToLower(), like));
            case "PlaceOfDelivery": return q.Where(x => EF.Functions.Like(x.PlaceOfDelivery.ToLower(), like));
            case "ReserveNumber": return q.Where(x => EF.Functions.Like(x.ReserveNumber.ToLower(), like));
            case "NationalMode": return q.Where(x => EF.Functions.Like(x.NationalMode.ToLower(), like));
            case "Winner": return q.Where(x => EF.Functions.Like(x.Winner.ToLower(), like));
            case "DeliveryTime": return q.Where(x => EF.Functions.Like(x.DeliveryTime.ToLower(), like));
            case "Description": return q.Where(x => EF.Functions.Like(x.Description.ToLower(), like));
            case "Note": return q.Where(x => EF.Functions.Like(x.Note.ToLower(), like));

            case "TypeOfPurchaseId":
                return int.TryParse(value, out var typeOfPurchaseId)
                    ? q.Where(x => x.TypeOfPurchaseId == typeOfPurchaseId)
                    : q;
            case "B2BStatusId":
                return int.TryParse(value, out var b2bStatusId)
                    ? q.Where(x => x.B2BStatusId == b2bStatusId)
                    : q;
            case "ExecutionStatusId":
                return int.TryParse(value, out var executionStatusId)
                    ? q.Where(x => x.ExecutionStatusId == executionStatusId)
                    : q;
            case "IsFinished":
                return bool.TryParse(value, out var isFinished)
                    ? q.Where(x => x.IsFinished == isFinished)
                    : q;

            case "BiddingDate":
                return TryParseDate(value, out var biddingDate)
                    ? q.Where(x => x.BiddingDate >= biddingDate && x.BiddingDate < biddingDate.AddDays(1))
                    : q;
            case "DateOfTransferForPlacement":
                return TryParseDate(value, out var transferDate)
                    ? q.Where(x => x.DateOfTransferForPlacement >= transferDate && x.DateOfTransferForPlacement < transferDate.AddDays(1))
                    : q;
            case "DateOfPlacement":
                return TryParseDate(value, out var placementDate)
                    ? q.Where(x => x.DateOfPlacement >= placementDate && x.DateOfPlacement < placementDate.AddDays(1))
                    : q;
            case "DateResults":
                return TryParseDate(value, out var resultsDate)
                    ? q.Where(x => x.DateResults >= resultsDate && x.DateResults < resultsDate.AddDays(1))
                    : q;
            case "DateOfConclusionOfTheContract":
                return TryParseDate(value, out var contractDate)
                    ? q.Where(x => x.DateOfConclusionOfTheContract >= contractDate && x.DateOfConclusionOfTheContract < contractDate.AddDays(1))
                    : q;

            case "NMCK":
                return ApplyMoneyFilter(q, value,
                    v => q.Where(x => x.NMCK == v),
                    v => q.Where(x => x.NMCK >= v),
                    v => q.Where(x => x.NMCK <= v));
            case "MinPrice":
                return ApplyMoneyFilter(q, value,
                    v => q.Where(x => x.MinPrice == v),
                    v => q.Where(x => x.MinPrice >= v),
                    v => q.Where(x => x.MinPrice <= v));
            case "ResultPrice":
                return ApplyMoneyFilter(q, value,
                    v => q.Where(x => x.ResultPrice == v),
                    v => q.Where(x => x.ResultPrice >= v),
                    v => q.Where(x => x.ResultPrice <= v));

            default:
                return q;
        }
    }

    private static IQueryable<ArchiveRegedit> ApplyArchiveColumnFilter(IQueryable<ArchiveRegedit> q, string key, string value)
    {
        var like = $"%{EscapeLike(value.ToLowerInvariant())}%";

        switch (key)
        {
            case "NameLink": return q.Where(x => EF.Functions.Like(x.NameLink.ToLower(), like));
            case "Customer": return q.Where(x => EF.Functions.Like(x.Customer.ToLower(), like));
            case "PlaceOfDelivery": return q.Where(x => EF.Functions.Like(x.PlaceOfDelivery.ToLower(), like));
            case "ReserveNumber": return q.Where(x => EF.Functions.Like(x.ReserveNumber.ToLower(), like));
            case "NationalMode": return q.Where(x => EF.Functions.Like(x.NationalMode.ToLower(), like));
            case "Winner": return q.Where(x => EF.Functions.Like(x.Winner.ToLower(), like));
            case "DeliveryTime": return q.Where(x => EF.Functions.Like(x.DeliveryTime.ToLower(), like));
            case "Description": return q.Where(x => EF.Functions.Like(x.Description.ToLower(), like));
            case "Note": return q.Where(x => EF.Functions.Like(x.Note.ToLower(), like));

            case "TypeOfPurchaseId":
                return int.TryParse(value, out var typeOfPurchaseId)
                    ? q.Where(x => x.TypeOfPurchaseId == typeOfPurchaseId)
                    : q;
            case "B2BStatusId":
                return int.TryParse(value, out var b2bStatusId)
                    ? q.Where(x => x.B2BStatusId == b2bStatusId)
                    : q;
            case "ExecutionStatusId":
                return int.TryParse(value, out var executionStatusId)
                    ? q.Where(x => x.ExecutionStatusId == executionStatusId)
                    : q;

            case "ArchivateDate":
                return TryParseDate(value, out var archivateDate)
                    ? q.Where(x => x.ArchivateDate >= archivateDate && x.ArchivateDate < archivateDate.AddDays(1))
                    : q;
            case "BiddingDate":
                return TryParseDate(value, out var biddingDate)
                    ? q.Where(x => x.BiddingDate >= biddingDate && x.BiddingDate < biddingDate.AddDays(1))
                    : q;

            default:
                return q;
        }
    }

    /// <summary>Фильтр по сумме: поддерживает "=", ">=", "<=", ">", "&lt;". Без знака — точное совпадение.</summary>
    private static IQueryable<T> ApplyMoneyFilter<T>(
        IQueryable<T> q,
        string value,
        Func<decimal, IQueryable<T>> equals,
        Func<decimal, IQueryable<T>> greaterOrEqual,
        Func<decimal, IQueryable<T>> lessOrEqual)
    {
        var op = "=";
        var text = value;

        if (value.StartsWith(">=", StringComparison.Ordinal)) { op = ">="; text = value[2..]; }
        else if (value.StartsWith("<=", StringComparison.Ordinal)) { op = "<="; text = value[2..]; }
        else if (value.StartsWith(">", StringComparison.Ordinal)) { op = ">="; text = value[1..]; }
        else if (value.StartsWith("<", StringComparison.Ordinal)) { op = "<="; text = value[1..]; }
        else if (value.StartsWith("=", StringComparison.Ordinal)) { op = "="; text = value[1..]; }

        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return q;
        }

        return op switch
        {
            ">=" => greaterOrEqual(parsed),
            "<=" => lessOrEqual(parsed),
            _ => equals(parsed)
        };
    }

    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    internal static bool TryParseDate(string value, out DateTime date)
    {
        string[] formats = { "yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy", "yyyyMMdd" };

        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date))
        {
            return true;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    // ------------------------------------------------------------------ сортировка

    private static IOrderedQueryable<Regedit> ApplySort(IQueryable<Regedit> source, string key, bool descending)
    {
        IOrderedQueryable<Regedit> Order<TKey>(Func<Regedit, TKey> selector) =>
            descending ? source.OrderByDescending(selector) : source.OrderBy(selector);

        return key switch
        {
            "NameLink" => Order(x => x.NameLink),
            "Customer" => Order(x => x.Customer),
            "PlaceOfDelivery" => Order(x => x.PlaceOfDelivery),
            "ReserveNumber" => Order(x => x.ReserveNumber),
            "NationalMode" => Order(x => x.NationalMode),
            "Winner" => Order(x => x.Winner),
            "DeliveryTime" => Order(x => x.DeliveryTime),
            "Description" => Order(x => x.Description),
            "Note" => Order(x => x.Note),
            "BiddingDate" => Order(x => x.BiddingDate),
            "DateOfTransferForPlacement" => Order(x => x.DateOfTransferForPlacement),
            "DateOfPlacement" => Order(x => x.DateOfPlacement),
            "DateResults" => Order(x => x.DateResults),
            "DateOfConclusionOfTheContract" => Order(x => x.DateOfConclusionOfTheContract),
            "NMCK" => Order(x => x.NMCK),
            "MinPrice" => Order(x => x.MinPrice),
            "ResultPrice" => Order(x => x.ResultPrice),
            "IsFinished" => Order(x => x.IsFinished),
            "TypeOfPurchaseId" => Order(x => x.TypeOfPurchase == null ? string.Empty : x.TypeOfPurchase.NameOfPurchase),
            "B2BStatusId" => Order(x => x.B2BStatus == null ? string.Empty : x.B2BStatus.NameB2B),
            "ExecutionStatusId" => Order(x => x.ExecutionStatus == null ? string.Empty : x.ExecutionStatus.NameExecution),
            _ => Order(x => x.Id)
        };
    }

    private static IOrderedQueryable<ArchiveRegedit> ApplyArchiveSort(IQueryable<ArchiveRegedit> source, string key, bool descending)
    {
        IOrderedQueryable<ArchiveRegedit> Order<TKey>(Func<ArchiveRegedit, TKey> selector) =>
            descending ? source.OrderByDescending(selector) : source.OrderBy(selector);

        return key switch
        {
            "NameLink" => Order(x => x.NameLink),
            "Customer" => Order(x => x.Customer),
            "PlaceOfDelivery" => Order(x => x.PlaceOfDelivery),
            "ReserveNumber" => Order(x => x.ReserveNumber),
            "NationalMode" => Order(x => x.NationalMode),
            "Winner" => Order(x => x.Winner),
            "DeliveryTime" => Order(x => x.DeliveryTime),
            "Description" => Order(x => x.Description),
            "Note" => Order(x => x.Note),
            "ArchivateDate" => Order(x => x.ArchivateDate),
            "BiddingDate" => Order(x => x.BiddingDate),
            "DateOfTransferForPlacement" => Order(x => x.DateOfTransferForPlacement),
            "DateOfPlacement" => Order(x => x.DateOfPlacement),
            "DateResults" => Order(x => x.DateResults),
            "DateOfConclusionOfTheContract" => Order(x => x.DateOfConclusionOfTheContract),
            "NMCK" => Order(x => x.NMCK),
            "MinPrice" => Order(x => x.MinPrice),
            "ResultPrice" => Order(x => x.ResultPrice),
            "TypeOfPurchaseId" => Order(x => x.TypeOfPurchase == null ? string.Empty : x.TypeOfPurchase.NameOfPurchase),
            "B2BStatusId" => Order(x => x.B2BStatus == null ? string.Empty : x.B2BStatus.NameB2B),
            "ExecutionStatusId" => Order(x => x.ExecutionStatus == null ? string.Empty : x.ExecutionStatus.NameExecution),
            _ => Order(x => x.Id)
        };
    }
}
