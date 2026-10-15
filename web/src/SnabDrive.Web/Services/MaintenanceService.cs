using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

public interface IMaintenanceService
{
    /// <summary>Полностью удалить все записи указанного набора данных. Возвращает число удалённых строк.</summary>
    Task<Result<int>> ClearAsync(string dataset, ChangeActor actor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Административная полная очистка наборов данных (реестр, архив, Контур-избранное, Контур-ловушка).
/// CellColors удаляется каскадно вместе с реестром; логи задач (WorkflowLogs) чистятся вместе с реестром.
/// </summary>
public sealed class MaintenanceService : IMaintenanceService
{
    public const string Registry = "registry";
    public const string Archive = "archive";
    public const string Kontur = "kontur";
    public const string Templates = "templates";

    private readonly IDbContextFactory<RegistryDbContext> _registryFactory;
    private readonly IAuditService _audit;

    public MaintenanceService(IDbContextFactory<RegistryDbContext> registryFactory, IAuditService audit)
    {
        _registryFactory = registryFactory;
        _audit = audit;
    }

    public async Task<Result<int>> ClearAsync(string dataset, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var db = await _registryFactory.CreateDbContextAsync(cancellationToken);

        int count;
        string label;
        switch (dataset)
        {
            case Registry:
                await db.RegistryWorkflowLogs.ExecuteDeleteAsync(cancellationToken); // логи задач реестра
                count = await db.Regedit.ExecuteDeleteAsync(cancellationToken);     // CellColors — каскадно
                label = "Реестр";
                break;
            case Archive:
                count = await db.ArchiveRegedit.ExecuteDeleteAsync(cancellationToken);
                label = "Архив";
                break;
            case Kontur:
                count = await db.KonturFavorites.ExecuteDeleteAsync(cancellationToken);
                label = "Контур — избранное";
                break;
            case Templates:
                count = await db.KonturTemplates.ExecuteDeleteAsync(cancellationToken);
                label = "Контур — ловушка";
                break;
            default:
                return Result<int>.Fail("Неизвестный набор данных.");
        }

        await _audit.WriteAsync("Cleared", dataset, 0, actor, null,
            $"Полная очистка: {label}. Удалено записей: {count}.", cancellationToken);

        return Result<int>.Ok(count);
    }
}
