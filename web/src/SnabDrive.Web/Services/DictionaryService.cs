using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

public sealed record DictionaryItemDto(int Id, string Name, string? ColorCode);

public interface IDictionaryService
{
    Task<IReadOnlyList<DictionaryItemDto>> GetTypesOfPurchaseAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DictionaryItemDto>> GetB2BStatusesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DictionaryItemDto>> GetExecutionStatusesAsync(CancellationToken cancellationToken = default);

    Task<Result<DictionaryItemDto>> CreateTypeOfPurchaseAsync(string name, string? colorCode, ChangeActor actor, CancellationToken cancellationToken = default);
    Task<Result<DictionaryItemDto>> UpdateTypeOfPurchaseAsync(int id, string name, string? colorCode, ChangeActor actor, CancellationToken cancellationToken = default);
    Task<Result> DeleteTypeOfPurchaseAsync(int id, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<Result<DictionaryItemDto>> CreateB2BStatusAsync(string name, ChangeActor actor, CancellationToken cancellationToken = default);
    Task<Result<DictionaryItemDto>> UpdateB2BStatusAsync(int id, string name, ChangeActor actor, CancellationToken cancellationToken = default);
    Task<Result> DeleteB2BStatusAsync(int id, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<Result<DictionaryItemDto>> CreateExecutionStatusAsync(string name, ChangeActor actor, CancellationToken cancellationToken = default);
    Task<Result<DictionaryItemDto>> UpdateExecutionStatusAsync(int id, string name, ChangeActor actor, CancellationToken cancellationToken = default);
    Task<Result> DeleteExecutionStatusAsync(int id, ChangeActor actor, CancellationToken cancellationToken = default);
}

/// <summary>Работа со справочниками: типы закупок, статусы B2B, статусы исполнения контракта.</summary>
public sealed class DictionaryService : IDictionaryService
{
    private readonly IDbContextFactory<RegistryDbContext> _factory;
    private readonly IAuditService _audit;
    private readonly IRegistryNotifier _notifier;

    public DictionaryService(IDbContextFactory<RegistryDbContext> factory, IAuditService audit, IRegistryNotifier notifier)
    {
        _factory = factory;
        _audit = audit;
        _notifier = notifier;
    }

    public async Task<IReadOnlyList<DictionaryItemDto>> GetTypesOfPurchaseAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.TypesOfPurchase.AsNoTracking()
            .OrderBy(x => x.NameOfPurchase)
            .Select(x => new DictionaryItemDto(x.Id, x.NameOfPurchase, x.ColorCode))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DictionaryItemDto>> GetB2BStatusesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.B2BStatuses.AsNoTracking()
            .OrderBy(x => x.NameB2B)
            .Select(x => new DictionaryItemDto(x.Id, x.NameB2B, null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DictionaryItemDto>> GetExecutionStatusesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.ExecutionStatuses.AsNoTracking()
            .OrderBy(x => x.NameExecution)
            .Select(x => new DictionaryItemDto(x.Id, x.NameExecution, null))
            .ToListAsync(cancellationToken);
    }

    // -------------------------------------------------- тип закупки

    public async Task<Result<DictionaryItemDto>> CreateTypeOfPurchaseAsync(string name, string? colorCode, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<DictionaryItemDto>.Invalid(new Dictionary<string, string> { ["name"] = "Укажите название" });
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = new TypeOfPurchase { NameOfPurchase = name.Trim(), ColorCode = colorCode?.Trim() ?? string.Empty };
        context.TypesOfPurchase.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        await AfterChangeAsync("TypeOfPurchase", entity.Id, AuditAction.Created, actor,
            $"Добавлен тип закупки «{entity.NameOfPurchase}»", cancellationToken);

        return Result<DictionaryItemDto>.Ok(new DictionaryItemDto(entity.Id, entity.NameOfPurchase, entity.ColorCode));
    }

    public async Task<Result<DictionaryItemDto>> UpdateTypeOfPurchaseAsync(int id, string name, string? colorCode, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<DictionaryItemDto>.Invalid(new Dictionary<string, string> { ["name"] = "Укажите название" });
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = await context.TypesOfPurchase.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result<DictionaryItemDto>.Fail("Тип закупки не найден.");
        }

        entity.NameOfPurchase = name.Trim();
        entity.ColorCode = colorCode?.Trim() ?? string.Empty;
        await context.SaveChangesAsync(cancellationToken);

        await AfterChangeAsync("TypeOfPurchase", id, AuditAction.Updated, actor,
            $"Изменён тип закупки «{entity.NameOfPurchase}»", cancellationToken);

        return Result<DictionaryItemDto>.Ok(new DictionaryItemDto(entity.Id, entity.NameOfPurchase, entity.ColorCode));
    }

    public async Task<Result> DeleteTypeOfPurchaseAsync(int id, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = await context.TypesOfPurchase.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result.Fail("Тип закупки не найден.");
        }

        var inUse = await context.Regedit.AnyAsync(x => x.TypeOfPurchaseId == id, cancellationToken) ||
                    await context.ArchiveRegedit.AnyAsync(x => x.TypeOfPurchaseId == id, cancellationToken);

        if (inUse)
        {
            return Result.Fail("Тип закупки используется в записях реестра — удаление невозможно.");
        }

        context.TypesOfPurchase.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);

        await AfterChangeAsync("TypeOfPurchase", id, AuditAction.Deleted, actor,
            $"Удалён тип закупки «{entity.NameOfPurchase}»", cancellationToken);

        return Result.Ok();
    }

    // -------------------------------------------------- статус B2B

    public async Task<Result<DictionaryItemDto>> CreateB2BStatusAsync(string name, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<DictionaryItemDto>.Invalid(new Dictionary<string, string> { ["name"] = "Укажите название" });
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = new B2BStatus { NameB2B = name.Trim() };
        context.B2BStatuses.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        await AfterChangeAsync("B2BStatus", entity.Id, AuditAction.Created, actor,
            $"Добавлен статус B2B «{entity.NameB2B}»", cancellationToken);

        return Result<DictionaryItemDto>.Ok(new DictionaryItemDto(entity.Id, entity.NameB2B, null));
    }

    public async Task<Result<DictionaryItemDto>> UpdateB2BStatusAsync(int id, string name, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<DictionaryItemDto>.Invalid(new Dictionary<string, string> { ["name"] = "Укажите название" });
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = await context.B2BStatuses.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result<DictionaryItemDto>.Fail("Статус B2B не найден.");
        }

        entity.NameB2B = name.Trim();
        await context.SaveChangesAsync(cancellationToken);

        await AfterChangeAsync("B2BStatus", id, AuditAction.Updated, actor,
            $"Изменён статус B2B «{entity.NameB2B}»", cancellationToken);

        return Result<DictionaryItemDto>.Ok(new DictionaryItemDto(entity.Id, entity.NameB2B, null));
    }

    public async Task<Result> DeleteB2BStatusAsync(int id, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = await context.B2BStatuses.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result.Fail("Статус B2B не найден.");
        }

        var inUse = await context.Regedit.AnyAsync(x => x.B2BStatusId == id, cancellationToken) ||
                    await context.ArchiveRegedit.AnyAsync(x => x.B2BStatusId == id, cancellationToken);

        if (inUse)
        {
            return Result.Fail("Статус B2B используется в записях реестра — удаление невозможно.");
        }

        context.B2BStatuses.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);

        await AfterChangeAsync("B2BStatus", id, AuditAction.Deleted, actor,
            $"Удалён статус B2B «{entity.NameB2B}»", cancellationToken);

        return Result.Ok();
    }

    // -------------------------------------------------- статус исполнения

    public async Task<Result<DictionaryItemDto>> CreateExecutionStatusAsync(string name, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<DictionaryItemDto>.Invalid(new Dictionary<string, string> { ["name"] = "Укажите название" });
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = new ExecutionStatus { NameExecution = name.Trim() };
        context.ExecutionStatuses.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        await AfterChangeAsync("ExecutionStatus", entity.Id, AuditAction.Created, actor,
            $"Добавлен статус исполнения «{entity.NameExecution}»", cancellationToken);

        return Result<DictionaryItemDto>.Ok(new DictionaryItemDto(entity.Id, entity.NameExecution, null));
    }

    public async Task<Result<DictionaryItemDto>> UpdateExecutionStatusAsync(int id, string name, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<DictionaryItemDto>.Invalid(new Dictionary<string, string> { ["name"] = "Укажите название" });
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = await context.ExecutionStatuses.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result<DictionaryItemDto>.Fail("Статус исполнения не найден.");
        }

        entity.NameExecution = name.Trim();
        await context.SaveChangesAsync(cancellationToken);

        await AfterChangeAsync("ExecutionStatus", id, AuditAction.Updated, actor,
            $"Изменён статус исполнения «{entity.NameExecution}»", cancellationToken);

        return Result<DictionaryItemDto>.Ok(new DictionaryItemDto(entity.Id, entity.NameExecution, null));
    }

    public async Task<Result> DeleteExecutionStatusAsync(int id, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var entity = await context.ExecutionStatuses.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result.Fail("Статус исполнения не найден.");
        }

        var inUse = await context.Regedit.AnyAsync(x => x.ExecutionStatusId == id, cancellationToken) ||
                    await context.ArchiveRegedit.AnyAsync(x => x.ExecutionStatusId == id, cancellationToken);

        if (inUse)
        {
            return Result.Fail("Статус исполнения используется в записях реестра — удаление невозможно.");
        }

        context.ExecutionStatuses.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);

        await AfterChangeAsync("ExecutionStatus", id, AuditAction.Deleted, actor,
            $"Удалён статус исполнения «{entity.NameExecution}»", cancellationToken);

        return Result.Ok();
    }

    private async Task AfterChangeAsync(string entityName, int entityId, string action, ChangeActor actor,
        string summary, CancellationToken cancellationToken)
    {
        await _audit.WriteAsync(action, entityName, entityId, actor, summary: summary, cancellationToken: cancellationToken);

        await _notifier.PublishAsync(new RegistryChangeEvent(
            ChangeKind.DictionaryChanged, entityName, entityId, actor.UserName, DateTime.UtcNow, summary), cancellationToken);
    }
}
