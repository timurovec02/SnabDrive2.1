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
/// Административная полная очистка наборов данных: реестр, архив, Контур-избранное, Контур-ловушка,
/// вложения, уведомления о торгах, справочники и журнал аудита. Пользователи (AspNet*) не затрагиваются.
/// CellColors удаляется каскадно вместе с реестром; логи задач, вложения и уведомления чистятся вместе с реестром.
/// </summary>
public sealed class MaintenanceService : IMaintenanceService
{
    public const string Registry = "registry";
    public const string Archive = "archive";
    public const string Kontur = "kontur";
    public const string Templates = "templates";
    public const string Audit = "audit";
    public const string Attachments = "attachments";
    public const string Notifications = "notifications";
    public const string Dictionaries = "dictionaries";

    private readonly IDbContextFactory<RegistryDbContext> _registryFactory;
    private readonly IDbContextFactory<AppIdentityDbContext> _identityFactory;
    private readonly IAuditService _audit;

    public MaintenanceService(IDbContextFactory<RegistryDbContext> registryFactory,
        IDbContextFactory<AppIdentityDbContext> identityFactory, IAuditService audit)
    {
        _registryFactory = registryFactory;
        _identityFactory = identityFactory;
        _audit = audit;
    }

    public async Task<Result<int>> ClearAsync(string dataset, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        // Журнал аудита хранится в отдельном контексте (AppIdentityDbContext).
        if (dataset == Audit)
        {
            await using var idb = await _identityFactory.CreateDbContextAsync(cancellationToken);
            var auditCount = await idb.AuditLog.ExecuteDeleteAsync(cancellationToken);
            // Запись о самой очистке не пишем, иначе журнал тут же перестанет быть пустым.
            return Result<int>.Ok(auditCount);
        }

        await using var db = await _registryFactory.CreateDbContextAsync(cancellationToken);

        int count;
        string label;
        switch (dataset)
        {
            case Registry:
                await db.RegistryWorkflowLogs.ExecuteDeleteAsync(cancellationToken); // логи задач реестра
                await db.BiddingNotifications.ExecuteDeleteAsync(cancellationToken); // уведомления о торгах
                await db.Attachments.ExecuteDeleteAsync(cancellationToken);          // вложения
                count = await db.Regedit.ExecuteDeleteAsync(cancellationToken);      // CellColors — каскадно
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
            case Attachments:
                count = await db.Attachments.ExecuteDeleteAsync(cancellationToken);
                label = "Вложения";
                break;
            case Notifications:
                count = await db.BiddingNotifications.ExecuteDeleteAsync(cancellationToken);
                label = "Уведомления о торгах";
                break;
            case Dictionaries:
                var b2b = await db.B2BStatuses.ExecuteDeleteAsync(cancellationToken);
                var types = await db.TypesOfPurchase.ExecuteDeleteAsync(cancellationToken);
                var exec = await db.ExecutionStatuses.ExecuteDeleteAsync(cancellationToken);
                var colors = await db.CellColors.ExecuteDeleteAsync(cancellationToken);
                count = b2b + types + exec + colors;
                label = "Справочники";
                break;
            default:
                return Result<int>.Fail("Неизвестный набор данных.");
        }

        await _audit.WriteAsync("Cleared", dataset, 0, actor, null,
            $"Полная очистка: {label}. Удалено записей: {count}.", cancellationToken);

        return Result<int>.Ok(count);
    }
}
