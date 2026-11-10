using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

public interface IMaintenanceService
{
    /// <summary>
    /// Полностью удалить все записи указанного набора данных. Возвращает число удалённых строк.
    /// </summary>
    /// <param name="resetIdentity">
    /// Сбросить счётчик Id, чтобы новые записи снова начинались с 1. По умолчанию да:
    /// после полной очистки в таблице никого не осталось, и старая нумерация только мешает.
    /// </param>
    Task<Result<int>> ClearAsync(string dataset, ChangeActor actor, bool resetIdentity = true, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Таблицы, у которых сбрасывается счётчик Id при очистке набора.
    /// Имена — константы: в SQL не попадает ничего из пользовательского ввода.
    /// </summary>
    private static readonly string[] RegistryTables =
    {
        "dbo.RegistryItems", "dbo.CellColors", "dbo.WorkflowLogs", "dbo.Attachments", "dbo.BiddingNotifications"
    };

    private static readonly string[] DictionaryTables =
    {
        "dbo.PurchaseTypes", "dbo.B2BStatuses", "dbo.ExecutionStatuses", "dbo.CellColors"
    };

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

    public async Task<Result<int>> ClearAsync(string dataset, ChangeActor actor, bool resetIdentity = true, CancellationToken cancellationToken = default)
    {
        // Журнал аудита хранится в отдельном контексте (AppIdentityDbContext).
        if (dataset == Audit)
        {
            await using var idb = await _identityFactory.CreateDbContextAsync(cancellationToken);
            var auditCount = await idb.AuditLog.ExecuteDeleteAsync(cancellationToken);
            if (resetIdentity)
            {
                await ReseedAsync(idb, new[] { "dbo.AuditLogs" }, cancellationToken);
            }

            // Запись о самой очистке не пишем, иначе журнал тут же перестанет быть пустым.
            return Result<int>.Ok(auditCount);
        }

        await using var db = await _registryFactory.CreateDbContextAsync(cancellationToken);

        int count;
        string label;
        string[] tables = Array.Empty<string>();
        switch (dataset)
        {
            case Registry:
                await db.RegistryWorkflowLogs.ExecuteDeleteAsync(cancellationToken); // логи задач реестра
                await db.BiddingNotifications.ExecuteDeleteAsync(cancellationToken); // уведомления о торгах
                await db.Attachments.ExecuteDeleteAsync(cancellationToken);          // вложения
                count = await db.Regedit.ExecuteDeleteAsync(cancellationToken);      // CellColors — каскадно

                // У избранного Контур ссылка RegeditId без внешнего ключа: если её не обнулить,
                // после сброса нумерации старая ссылка будет показывать на чужую закупку.
                await db.KonturFavorites
                    .Where(f => f.RegeditId != null)
                    .ExecuteUpdateAsync(x => x.SetProperty(f => f.RegeditId, (int?)null), cancellationToken);

                tables = RegistryTables;
                label = "Реестр";
                break;
            case Archive:
                count = await db.ArchiveRegedit.ExecuteDeleteAsync(cancellationToken);
                tables = new[] { "dbo.ArchiveRegistryItems" };
                label = "Архив";
                break;
            case Kontur:
                count = await db.KonturFavorites.ExecuteDeleteAsync(cancellationToken);
                tables = new[] { "dbo.KonturFavorites" };
                label = "Контур — избранное";
                break;
            case Templates:
                count = await db.KonturTemplates.ExecuteDeleteAsync(cancellationToken);
                tables = new[] { "dbo.KonturTemplates" };
                label = "Контур — шаблоны";
                break;
            case Attachments:
                count = await db.Attachments.ExecuteDeleteAsync(cancellationToken);
                tables = new[] { "dbo.Attachments" };
                label = "Вложения";
                break;
            case Notifications:
                count = await db.BiddingNotifications.ExecuteDeleteAsync(cancellationToken);
                tables = new[] { "dbo.BiddingNotifications" };
                label = "Уведомления о торгах";
                break;
            case Dictionaries:
                {
                    var b2b = await db.B2BStatuses.ExecuteDeleteAsync(cancellationToken);
                    var types = await db.TypesOfPurchase.ExecuteDeleteAsync(cancellationToken);
                    var exec = await db.ExecutionStatuses.ExecuteDeleteAsync(cancellationToken);
                    var colors = await db.CellColors.ExecuteDeleteAsync(cancellationToken);
                    count = b2b + types + exec + colors;
                }

                tables = DictionaryTables;
                label = "Справочники";
                break;
            default:
                return Result<int>.Fail("Неизвестный набор данных.");
        }

        var reseeded = resetIdentity && tables.Length > 0 ? await ReseedAsync(db, tables, cancellationToken) : 0;

        var summary = $"Полная очистка: {label}. Удалено записей: {count}." +
                      (reseeded > 0 ? $" Нумерация сброшена в таблицах: {reseeded} — новые записи снова с Id = 1." : string.Empty);

        await _audit.WriteAsync("Cleared", dataset, 0, actor, null, summary, cancellationToken);

        return Result<int>.Ok(count);
    }

    /// <summary>
    /// Сбрасывает счётчик IDENTITY, чтобы следующая запись снова получила Id = 1.
    /// DELETE, в отличие от TRUNCATE, счётчик не трогает, а TRUNCATE здесь не годится:
    /// на dbo.RegistryItems ссылается внешний ключ dbo.CellColors — SQL Server не даст
    /// обнулить таблицу, даже если она уже пуста.
    /// Таблицу без колонки IDENTITY просто пропускаем — DBCC на такой таблице падает с ошибкой.
    /// </summary>
    private static async Task<int> ReseedAsync(DbContext db, string[] tables, CancellationToken cancellationToken)
    {
        // Все таблицы из списка только что очищены этим же методом, поэтому RESEED, 0
        // не может разойтись с уже существующими строками.
        var sql = new System.Text.StringBuilder();
        foreach (var table in tables)
        {
            // Колонку IDENTITY ищем в sys.columns, а не по имени: у справочников она
            // называется [ID], у остальных — [Id]. Нет таблицы или нет счётчика — пропускаем,
            // потому что DBCC CHECKIDENT на такой таблице падает с ошибкой.
            sql.Append("IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('")
               .Append(table).Append("', 'U') AND is_identity = 1) DBCC CHECKIDENT ('")
               .Append(table).Append("', RESEED, 0);");
        }

        await db.Database.ExecuteSqlRawAsync(sql.ToString(), cancellationToken);
        return tables.Length;
    }
}
