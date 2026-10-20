using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

/// <summary>Стадии воркфлоу (Id, название). Порядок в списке = порядок отображения.</summary>
public static class WorkflowStages
{
    public static readonly (int Id, string Name)[] All =
    {
        (1, "1. Контур и подготовка"),
        (2, "1.2 Резерв: дозаполнение"),
        (12, "1.5 Согласование"),
        (3, "2. Подача заявки на площадку"),
        (4, "3. Торги"),
        (5, "4. Перемещение товара"),
        (6, "5. Контракт подписан"),
        (7, "6. Логистические документы"),
        (8, "7. Ожидание доставки"),
        (9, "8. Приемка заказчиком"),
        (10, "9. Оплата заказчиком"),
        (11, "Завершена"),
        (0, "Проиграна")
    };

    public static string Name(int id) => All.FirstOrDefault(x => x.Id == id).Name ?? "—";
}

/// <summary>Данные закупки для вкладки «Задачи» (читаемые + редактируемые поля).</summary>
public sealed record WorkflowRow(
    int Id,
    string NameLink,
    string Customer,
    decimal NMCK,
    DateTime? BiddingDate,
    string ReserveNumber,
    string? Place,
    string PlaceOfDelivery,
    decimal ResultPrice,
    string Winner,
    string Note,
    string? Attachments,
    int Stage,
    bool IsLost);

/// <summary>Модель редактирования строки воркфлоу.</summary>
public sealed class WorkflowEdit
{
    public string NameLink { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public decimal NMCK { get; set; }
    public string ReserveNumber { get; set; } = string.Empty;
    public string Place { get; set; } = string.Empty;
    public string PlaceOfDelivery { get; set; } = string.Empty;
    public decimal ResultPrice { get; set; }
    public string Winner { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Attachments { get; set; } = string.Empty;
}

public interface IWorkflowService
{
    Task<IReadOnlyList<WorkflowRow>> GetAsync(CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(int regeditId, WorkflowEdit edit, CancellationToken cancellationToken = default);

    /// <summary>Перевести на стадию по коду перехода (см. WorkflowService.Transitions).</summary>
    Task<Result> AdvanceAsync(int regeditId, int code, ChangeActor actor, CancellationToken cancellationToken = default);

    /// <summary>Сохранить файл во вложения закупки; возвращает обновлённый список имён.</summary>
    Task<Result<string>> SaveAttachmentAsync(int regeditId, string fileName, Stream content, CancellationToken cancellationToken = default);

    Task<Result> DeleteAttachmentAsync(int regeditId, string attachmentPath, CancellationToken cancellationToken = default);
}

public sealed class WorkflowService : IWorkflowService
{
    // код перехода -> (целевая стадия, проиграна)
    private static readonly Dictionary<int, (int To, bool Lost)> Transitions = new()
    {
        [1] = (3, false),   // подготовка -> подача заявки
        [2] = (2, false),   // подготовка -> дозаполнить резерв
        [3] = (12, false),  // резерв -> согласование
        [13] = (3, false),  // согласование -> подача заявки
        [4] = (4, false),   // подача -> торги
        [5] = (5, false),   // торги -> выиграл (перемещение)
        [6] = (0, true),    // торги -> проигран
        [7] = (6, false),   // перемещение -> подписан
        [8] = (7, false),   // подписан -> логистика
        [9] = (8, false),   // логистика -> ожидание доставки
        [10] = (9, false),  // доставка -> приемка
        [11] = (10, false), // приемка -> оплата
        [12] = (11, false)  // оплата -> завершена
    };

    private readonly IDbContextFactory<RegistryDbContext> _factory;
    private readonly BitrixNotifier _bitrix;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<WorkflowService> _logger;
    private readonly IRolePermissionService _rolePerms;
    private readonly UserManager<ApplicationUser> _users;

    public WorkflowService(IDbContextFactory<RegistryDbContext> factory, BitrixNotifier bitrix,
        IWebHostEnvironment env, ILogger<WorkflowService> logger,
        IRolePermissionService rolePerms, UserManager<ApplicationUser> users)
    {
        _factory = factory;
        _bitrix = bitrix;
        _env = env;
        _logger = logger;
        _rolePerms = rolePerms;
        _users = users;
    }

    public async Task<IReadOnlyList<WorkflowRow>> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.Regedit.AsNoTracking()
            .OrderBy(r => r.Stage).ThenByDescending(r => r.Id)
            .Select(r => new WorkflowRow(r.Id, r.NameLink, r.Customer, r.NMCK, r.BiddingDate,
                r.ReserveNumber, r.Place, r.PlaceOfDelivery, r.ResultPrice, r.Winner, r.Note,
                r.Attachments, r.Stage, r.IsLost))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result> UpdateAsync(int regeditId, WorkflowEdit edit, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result.Fail("Запись не найдена.");
        }

        item.NameLink = edit.NameLink;
        item.Customer = edit.Customer;
        item.NMCK = edit.NMCK;
        item.ReserveNumber = edit.ReserveNumber;
        item.Place = edit.Place;
        item.PlaceOfDelivery = edit.PlaceOfDelivery;
        item.ResultPrice = edit.ResultPrice;
        item.Winner = edit.Winner;
        item.Note = edit.Note;
        item.Attachments = edit.Attachments;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> AdvanceAsync(int regeditId, int code, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (!Transitions.TryGetValue(code, out var tr))
        {
            return Result.Fail("Неизвестный переход.");
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result.Fail("Запись не найдена.");
        }

        var from = item.Stage;
        item.Stage = tr.To;
        item.IsLost = tr.Lost;

        context.RegistryWorkflowLogs.Add(new RegistryWorkflowLog
        {
            RegeditId = regeditId,
            FromStage = from,
            ToStage = tr.To,
            Action = WorkflowStages.Name(tr.To),
            ByUser = actor.UserName,
            AtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync(cancellationToken);

        await NotifyStageOwnersAsync(tr.To, item, cancellationToken);

        _logger.LogInformation("Воркфлоу: закупка {Id} {From}->{To} ({User})", regeditId, from, tr.To, actor.UserName);
        return Result.Ok();
    }

    /// <summary>
    /// Личные уведомления в Битрикс24 пользователям ролей, отвечающих за стадию <paramref name="stageId"/>.
    /// Роль отвечает за стадию, если в её настройке (/roles → «Стадии воркфлоу») стадия отмечена
    /// либо набор стадий пуст (= все стадии). Администратор (техобслуживание) не уведомляется.
    /// Получатель — пользователь с заполненным BitrixUserId.
    /// </summary>
    private async Task NotifyStageOwnersAsync(int stageId, Regedit item, CancellationToken cancellationToken)
    {
        var stageName = WorkflowStages.Name(stageId);
        var title = $"Задача на этапе: {stageName}";
        var body =
            $"{item.NameLink}\n" +
            $"Заказчик: {item.Customer}\n" +
            $"НМЦК: {item.NMCK:# ##0.00}\n" +
            $"Этап: {stageName}\n" +
            $"Запись #{item.Id}";

        try
        {
            var roleNames = await _rolePerms.GetAllRoleNamesAsync(cancellationToken);
            var notified = new HashSet<string>(StringComparer.Ordinal);

            foreach (var role in roleNames)
            {
                if (string.Equals(role, AppRoles.Admin, StringComparison.Ordinal))
                {
                    continue; // администратор — техобслуживание, бизнес-уведомления не получает
                }

                var set = await _rolePerms.GetAsync(role, cancellationToken);
                if (!set.StageEnabled(stageId))
                {
                    continue;
                }

                var users = await _users.GetUsersInRoleAsync(role);
                foreach (var user in users)
                {
                    var bitrixId = user.BitrixUserId;
                    if (string.IsNullOrWhiteSpace(bitrixId) || !notified.Add(bitrixId))
                    {
                        continue; // нет ID Битрикс или уже уведомили (пользователь в нескольких ролях)
                    }

                    await _bitrix.NotifyAsync(title, body, cancellationToken, bitrixId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось разослать уведомления Битрикс24 по стадии {Stage}", stageId);
        }
    }

    public async Task<Result<string>> SaveAttachmentAsync(int regeditId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result<string>.Fail("Запись не найдена.");
        }

        var dir = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "uploads", regeditId.ToString());
        Directory.CreateDirectory(dir);

        var safeName = Path.GetFileName(fileName);
        var stored = $"{Guid.NewGuid():N}_{safeName}";
        await using (var fs = new FileStream(Path.Combine(dir, stored), FileMode.Create, FileAccess.Write))
        {
            await content.CopyToAsync(fs, cancellationToken);
        }

        var relative = $"/uploads/{regeditId}/{stored}";
        var list = string.IsNullOrWhiteSpace(item.Attachments)
            ? relative
            : item.Attachments + ";" + relative;
        item.Attachments = list;
        await context.SaveChangesAsync(cancellationToken);

        return Result<string>.Ok(list);
    }

    public async Task<Result> DeleteAttachmentAsync(int regeditId, string attachmentPath, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result.Fail("Запись не найдена.");
        }

        var parts = (item.Attachments ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != attachmentPath)
            .ToList();
        item.Attachments = string.Join(";", parts);

        try
        {
            var root = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var rel = attachmentPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var full = Path.Combine(root, rel);
            if (File.Exists(full))
            {
                File.Delete(full);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось удалить файл вложения {Path}", attachmentPath);
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
