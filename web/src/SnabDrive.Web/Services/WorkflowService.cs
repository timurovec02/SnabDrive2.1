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
        (12, "2. Согласование"),
        (3, "3. Подача заявки на площадку"),
        (4, "4. Торги"),
        (5, "5. Перемещение товара"),
        (6, "6. Контракт"),
        (7, "7. Логистические документы"),
        (8, "8. Ожидание доставки"),
        (9, "9. Приемка заказчиком"),
        (10, "10. Оплата заказчиком"),
        (11, "Завершена"),
        (0, "Проиграна"),
        (13, "Проблемы с приемкой")
    };

    public static string Name(int id) => All.FirstOrDefault(x => x.Id == id).Name ?? "—";
}

/// <summary>Статусы контракта (этап 6 «Контракт»).</summary>
public static class ContractStatuses
{
    public static readonly (int Id, string Name)[] All =
    {
        (1, "На подписании у Снабдрайв"),
        (2, "На подписании у заказчика"),
        (3, "Подписан"),
        (4, "Нужен протокол"),
        (5, "Нужен допник")
    };

    public static string Name(int? id) => All.FirstOrDefault(x => x.Id == id).Name ?? string.Empty;
}

/// <summary>Данные закупки для вкладки «Задачи» (читаемые + редактируемые поля).</summary>
public sealed record WorkflowRow(
    int Id,
    string NameLink,
    string? EisLink,
    string Customer,
    decimal NMCK,
    DateTime? BiddingDate,
    string ReserveNumber,
    string? Place,
    string PlaceOfDelivery,
    decimal ResultPrice,
    string Note,
    string? Attachments,
    int Stage,
    bool IsLost,
    int? TypeOfPurchaseId,
    string? TypeOfPurchaseName,
    int? B2BStatusId,
    string? B2BStatusName,
    string NationalMode,
    decimal MinPrice,
    DateTime? DeliveryDate,
    bool IsApproved,
    bool IsCall,
    string PurchaseNumber,
    bool IsByRequest,
    DateTime? ExpectedDeliveryDate,
    int? BranchId,
    string? BranchFilePath,
    string? ReturnReason,
    int? ContractStatusId,
    bool IsContractChecked,
    bool IsEisFilled,
    DateTime? StageStartedAtUtc,
    string? StageStartedBy,
    DateTime? StageCompletedAtUtc,
    string? StageCompletedBy,
    DateTime? ApplicationEndDate,
    DateTime? ContractClosedAtUtc,
    string? ContractClosedBy,
    bool PaymentOverdue,
    DateTime? Stage5ClosedAtUtc,
    string? Stage5ClosedBy,
    string? DeliveryTerms);

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
    public string Note { get; set; } = string.Empty;
    public string Attachments { get; set; } = string.Empty;

    // Поля, доступные на стадиях 1 / 1.2 / 2 (Согласование):
    public int? TypeOfPurchaseId { get; set; }
    public int? B2BStatusId { get; set; }
    public string NationalMode { get; set; } = string.Empty;
    public DateTime? BiddingDate { get; set; }

    /// <summary>Окончание приема заявок (редактируется на этапах 1 / 1.2 / 2).</summary>
    public DateTime? ApplicationEndDate { get; set; }
    public decimal MinPrice { get; set; }

    /// <summary>Дата доставки (бывший «Срок поставки»).</summary>
    public DateTime? DeliveryDate { get; set; }

    /// <summary>«Сроки доставки» — текст (этап 1 и этапы 6 — «Завершена»).</summary>
    public string DeliveryTerms { get; set; } = string.Empty;

    /// <summary>Флажок «Звони» (стадия 2 — Согласование).</summary>
    public bool IsCall { get; set; }

    /// <summary>Флажок «По заявкам» (стадия 1).</summary>
    public bool IsByRequest { get; set; }

    /// <summary>Ожидаемая дата доставки (этапы 5/7).</summary>
    public DateTime? ExpectedDeliveryDate { get; set; }

    /// <summary>Выбранный филиал (этап 7).</summary>
    public int? BranchId { get; set; }

    /// <summary>Статус контракта (этап 6): см. <see cref="ContractStatuses"/>.</summary>
    public int? ContractStatusId { get; set; }

    /// <summary>Флажок «Проверен» (этап 6 — контракт).</summary>
    public bool IsContractChecked { get; set; }

    /// <summary>Флажок «Заполнить в ЕИС» (этап 7) — подсвечивает дату доставки на этапе 8.</summary>
    public bool IsEisFilled { get; set; }

    /// <summary>Флажок «Оплата просрочена» (этап 10 — Оплата).</summary>
    public bool PaymentOverdue { get; set; }
}

public interface IWorkflowService
{
    Task<IReadOnlyList<WorkflowRow>> GetAsync(CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(int regeditId, WorkflowEdit edit, ChangeActor? actor = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавить пустую закупку сразу на первую стадию — кнопка «Добавить» во вкладке «Задачи».
    /// Возвращает идентификатор новой строки реестра.
    /// </summary>
    Task<Result<int>> CreateAsync(ChangeActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Закрыть этап 6 «Контракт» вручную. Этап идёт параллельно с этапом 5 «Перемещение
    /// товара», поэтому стадия закупки не меняется — фиксируются только дата и исполнитель.
    /// Без закрытого контракта этап 5 не завершается (см. проверку в <see cref="AdvanceAsync"/>).
    /// </summary>
    Task<Result> CloseContractAsync(int regeditId, ChangeActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Завершить этап 5 «Перемещение товара». Закупка сразу пропадает из вкладки этапа 5,
    /// а на этап 7 попадает только после закрытия этапа 6 «Контракт»: если контракт уже
    /// закрыт, перевод происходит немедленно, иначе — в <see cref="CloseContractAsync"/>.
    /// </summary>
    Task<Result> CompleteMovementAsync(int regeditId, ChangeActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Подсветка полей карточки «Задачи» (правая кнопка мыши). Хранится в той же таблице
    /// [CellColors], что и подсветка ячеек реестра, поэтому новая схема не нужна.
    /// Ключ словаря — «идентификатор|поле», см. <see cref="FieldColorKey"/>.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetFieldColorsAsync(IEnumerable<int> regeditIds, CancellationToken cancellationToken = default);

    /// <summary>Покрасить рамку поля; пустой <paramref name="colorCode"/> снимает подсветку.</summary>
    Task<Result> SetFieldColorAsync(int regeditId, string fieldKey, string? colorCode, ChangeActor actor, CancellationToken cancellationToken = default);

    /// <summary>Перевести на стадию по коду перехода (см. WorkflowService.Transitions).</summary>
    Task<Result> AdvanceAsync(int regeditId, int code, ChangeActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Перевести закупку на любую стадию вручную — доступно пользователю с правом
    /// «Менять стадию закупок» (см. StageChangeClaim).
    /// </summary>
    Task<Result> SetStageAsync(int regeditId, int stage, ChangeActor actor, CancellationToken cancellationToken = default);

    /// <summary>Сохранить файл во вложения закупки; возвращает обновлённый список имён.</summary>
    Task<Result<string>> SaveAttachmentAsync(int regeditId, string fileName, Stream content, CancellationToken cancellationToken = default);

    Task<Result> DeleteAttachmentAsync(int regeditId, string attachmentPath, CancellationToken cancellationToken = default);

    /// <summary>Этап 7: сохранить отдельный файл для оповещения филиала.</summary>
    Task<Result<string>> SaveBranchFileAsync(int regeditId, string fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Этап 7: оповестить выбранный филиал в Битрикс24 (со ссылкой на файл) и перевести на следующий этап.</summary>
    Task<Result> NotifyBranchAsync(int regeditId, ChangeActor actor, CancellationToken cancellationToken = default);

    /// <summary>Этап 9: зафиксировать проблему с приемкой — переводит закупку на стадию «Проблемы с приемкой».</summary>
    Task<Result> ReturnAsync(int regeditId, string reason, ChangeActor actor, CancellationToken cancellationToken = default);

    /// <summary>Количество закупок с торгами завтра и сегодня (для всплывающего уведомления).</summary>
    Task<(int Tomorrow, int Today)> GetUpcomingBiddingCountsAsync(CancellationToken cancellationToken = default);
}

public sealed class WorkflowService : IWorkflowService
{
    // код перехода -> (целевая стадия, проиграна)
    private static readonly Dictionary<int, (int To, bool Lost)> Transitions = new()
    {
        [1] = (3, false),   // подготовка -> подача заявки (с 1-й стадии больше не используется)
        [2] = (2, false),   // подготовка -> 1.2 дозаполнить резерв
        [14] = (12, false), // подготовка -> 2 согласование
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
    private readonly IRegistryNotifier _notifier;

    public WorkflowService(IDbContextFactory<RegistryDbContext> factory, BitrixNotifier bitrix,
        IWebHostEnvironment env, ILogger<WorkflowService> logger,
        IRolePermissionService rolePerms, UserManager<ApplicationUser> users,
        IRegistryNotifier notifier)
    {
        _factory = factory;
        _bitrix = bitrix;
        _env = env;
        _logger = logger;
        _rolePerms = rolePerms;
        _users = users;
        _notifier = notifier;
    }

    /// <summary>
    /// Рассылает событие изменения: по внутрипроцессной шине мгновенно обновляются открытые
    /// страницы («Задачи», «Реестр»), параллельно событие уходит клиентам SignalR.
    /// </summary>
    private Task NotifyAsync(int regeditId, string userName, string summary, CancellationToken cancellationToken) =>
        _notifier.PublishAsync(new RegistryChangeEvent(
            ChangeKind.Updated, RegistryService.EntityName, regeditId, userName, DateTime.UtcNow, summary),
            cancellationToken);

    private static string ShortName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "без наименования" : (value.Length <= 60 ? value : value[..57] + "…");

    public async Task<IReadOnlyList<WorkflowRow>> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.Regedit.AsNoTracking()
            .OrderBy(r => r.Stage).ThenByDescending(r => r.Id)
            .Select(r => new WorkflowRow(r.Id, r.NameLink, r.EisLink, r.Customer, r.NMCK, r.BiddingDate,
                r.ReserveNumber, r.Place, r.PlaceOfDelivery, r.ResultPrice, r.Note,
                r.Attachments, r.Stage, r.IsLost,
                r.TypeOfPurchaseId, r.TypeOfPurchase != null ? r.TypeOfPurchase.NameOfPurchase : null,
                r.B2BStatusId, r.B2BStatus != null ? r.B2BStatus.NameB2B : null,
                r.NationalMode, r.MinPrice, r.DeliveryDate, r.IsApproved, r.IsCall, r.PurchaseNumber,
                r.IsByRequest, r.ExpectedDeliveryDate, r.BranchId, r.BranchFilePath, r.ReturnReason,
                r.ContractStatusId, r.IsContractChecked, r.IsEisFilled,
                r.StageStartedAtUtc, r.StageStartedBy, r.StageCompletedAtUtc, r.StageCompletedBy,
                r.ApplicationEndDate,
                r.ContractClosedAtUtc, r.ContractClosedBy, r.PaymentOverdue,
                r.Stage5ClosedAtUtc, r.Stage5ClosedBy, r.DeliveryTerms))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result> UpdateAsync(int regeditId, WorkflowEdit edit, ChangeActor? actor = null, CancellationToken cancellationToken = default)
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
        item.Note = edit.Note;
        item.Attachments = edit.Attachments;

        // Поля стадий 1 / 1.2 / 2 (Согласование). На более поздних стадиях они
        // приходят неизменёнными (форма инициализирует их текущими значениями).
        item.TypeOfPurchaseId = edit.TypeOfPurchaseId;
        item.B2BStatusId = edit.B2BStatusId;
        item.NationalMode = edit.NationalMode;
        item.BiddingDate = edit.BiddingDate;
        item.ApplicationEndDate = edit.ApplicationEndDate;
        item.MinPrice = edit.MinPrice;
        item.DeliveryDate = edit.DeliveryDate;
        item.DeliveryTerms = string.IsNullOrWhiteSpace(edit.DeliveryTerms) ? null : edit.DeliveryTerms.Trim();
        item.IsCall = edit.IsCall;
        item.IsByRequest = edit.IsByRequest;
        item.PaymentOverdue = edit.PaymentOverdue;
        item.ExpectedDeliveryDate = edit.ExpectedDeliveryDate;
        item.BranchId = edit.BranchId;

        // Этап 6 «Контракт»: статус + отметка «Проверен»; этап 7 — «Заполнить в ЕИС».
        item.ContractStatusId = edit.ContractStatusId;
        item.IsContractChecked = edit.IsContractChecked;
        item.IsEisFilled = edit.IsEisFilled;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить изменения закупки {RegeditId}", regeditId);
            return Result.Fail("Не удалось сохранить изменения: " + ErrorText.Describe(ex));
        }

        var who = actor?.UserName ?? "Пользователь";
        await NotifyAsync(regeditId, who,
            $"{who} изменил(а) закупку «{ShortName(item.NameLink)}» (этап «{WorkflowStages.Name(item.Stage)}»)",
            cancellationToken);

        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result<int>> CreateAsync(ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var item = new Regedit
        {
            Stage = 1, // новая закупка всегда появляется на первой стадии
            StageStartedAtUtc = DateTime.UtcNow,
            StageStartedBy = actor.UserName
        };
        context.Regedit.Add(item);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось добавить закупку ({User})", actor.UserName);
            return Result<int>.Fail("Не удалось добавить закупку: " + ErrorText.Describe(ex));
        }

        // Журнал воркфлоу: идентификатор строки известен только после первой записи.
        context.RegistryWorkflowLogs.Add(new RegistryWorkflowLog
        {
            RegeditId = item.Id,
            FromStage = 0,
            ToStage = 1,
            Action = JournalText.Action("Закупка добавлена"),
            ByUser = actor.UserName,
            AtUtc = DateTime.UtcNow
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Строка уже создана — сообщаем об этом, но не теряем её идентификатор.
            _logger.LogError(ex, "Закупка {Id} создана, но запись журнала не сохранилась", item.Id);
        }

        return Result<int>.Ok(item.Id);
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

        // Проверка этапа 4 «Торги»: итог торгов нельзя зафиксировать, пока не указаны
        // место и итоговая цена — иначе на следующие этапы уйдут пустые данные.
        if (from == 4 && code is 5 or 6)
        {
            if (string.IsNullOrWhiteSpace(item.Place))
            {
                return Result.Fail("Укажите «Место на торгах» — без него нельзя зафиксировать итог торгов.");
            }

            if (item.ResultPrice <= 0)
            {
                return Result.Fail("Укажите «Итог цена» — без неё нельзя зафиксировать итог торгов.");
            }
        }

        // Этап 5 «Перемещение товара» завершается только после того, как вручную закрыт
        // параллельный этап 6 «Контракт» (код 8 из этапа 6 — прежний путь, его не трогаем).
        if (code == 8 && from == 5 && item.ContractClosedAtUtc is null)
        {
            return Result.Fail("Сначала закройте этап «6. Контракт» — этап 5 завершается только после него.");
        }

        // Авто-даты при завершении этапа (дата + время):
        //  1 «Контур и подготовка»      -> «Дата передачи на размещение»;
        //  3 «Подача заявки на площадку» -> «Дата размещения на площадке».
        var now = DateTime.Now;
        if (from == 1)
        {
            item.DateOfTransferForPlacement = now;
        }
        if (from == 3)
        {
            item.DateOfPlacement = now;
        }

        // Кто и когда завершил этап — фиксируется для учёта работы сотрудников.
        StageTracker.MoveTo(item, tr.To, actor.UserName);

        context.RegistryWorkflowLogs.Add(new RegistryWorkflowLog
        {
            RegeditId = regeditId,
            FromStage = from,
            ToStage = tr.To,
            Action = JournalText.Action(WorkflowStages.Name(tr.To)),
            ByUser = actor.UserName,
            AtUtc = DateTime.UtcNow
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить перевод закупки {RegeditId} на этап {ToStage}", regeditId, tr.To);
            return Result.Fail("Не удалось перевести закупку на следующий этап: " + ErrorText.Describe(ex));
        }

        await NotifyStageOwnersAsync(tr.To, item, cancellationToken);

        await NotifyAsync(regeditId, actor.UserName,
            $"{actor.UserName}: «{ShortName(item.NameLink)}» → стадия «{WorkflowStages.Name(tr.To)}»",
            cancellationToken);

        _logger.LogInformation("Воркфлоу: закупка {Id} {From}->{To} ({User})", regeditId, from, tr.To, actor.UserName);
        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result> CloseContractAsync(int regeditId, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result.Fail("Запись не найдена.");
        }

        if (item.ContractClosedAtUtc is not null)
        {
            return Result.Fail($"Этап «6. Контракт» уже закрыт: {item.ContractClosedAtUtc:dd.MM.yyyy HH:mm}, {item.ContractClosedBy}.");
        }

        var now = DateTime.UtcNow;
        item.ContractClosedAtUtc = now;
        item.ContractClosedBy = actor.UserName;

        // Стадия не меняется: контракт закрывается внутри этапа 5, поэтому в журнале
        // FromStage и ToStage совпадают.
        context.RegistryWorkflowLogs.Add(new RegistryWorkflowLog
        {
            RegeditId = regeditId,
            FromStage = item.Stage,
            ToStage = item.Stage,
            Action = JournalText.Action("Этап 6 «Контракт» закрыт"),
            ByUser = actor.UserName,
            AtUtc = now
        });

        // Если этап 5 уже завершён, контракт был последним — закупка уходит на этап 7.
        var moved = TryMoveToLogistics(item, context, actor, now);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось закрыть этап «Контракт» у закупки {RegeditId}", regeditId);
            return Result.Fail("Не удалось закрыть этап «Контракт»: " + ErrorText.Describe(ex));
        }

        await NotifyAsync(regeditId, actor.UserName,
            moved
                ? $"{actor.UserName}: «{ShortName(item.NameLink)}» — контракт закрыт, закупка на этапе «{WorkflowStages.Name(7)}»"
                : $"{actor.UserName}: «{ShortName(item.NameLink)}» — этап «6. Контракт» закрыт",
            cancellationToken);

        _logger.LogInformation("Воркфлоу: закупка {Id} — этап «6. Контракт» закрыт, перевод на 7: {Moved} ({User})",
            regeditId, moved, actor.UserName);
        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result> CompleteMovementAsync(int regeditId, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result.Fail("Запись не найдена.");
        }

        if (item.Stage != 5)
        {
            return Result.Fail("Этап 5 завершается только у закупки на этапе «5. Перемещение товара».");
        }

        if (item.Stage5ClosedAtUtc is not null)
        {
            return Result.Fail($"Этап 5 уже завершён: {item.Stage5ClosedAtUtc:dd.MM.yyyy HH:mm}, {item.Stage5ClosedBy}.");
        }

        var now = DateTime.UtcNow;
        item.Stage5ClosedAtUtc = now;
        item.Stage5ClosedBy = actor.UserName;

        // Стадия пока не меняется: закупка ждёт закрытия контракта, поэтому From = To.
        context.RegistryWorkflowLogs.Add(new RegistryWorkflowLog
        {
            RegeditId = regeditId,
            FromStage = 5,
            ToStage = 5,
            Action = JournalText.Action("Этап 5 «Перемещение товара» завершён"),
            ByUser = actor.UserName,
            AtUtc = now
        });

        // Контракт уже закрыт — ждать нечего, уходим на этап 7 сразу.
        var moved = TryMoveToLogistics(item, context, actor, now);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось завершить этап «Перемещение товара» у закупки {RegeditId}", regeditId);
            return Result.Fail("Не удалось завершить этап 5: " + ErrorText.Describe(ex));
        }

        await NotifyAsync(regeditId, actor.UserName,
            moved
                ? $"{actor.UserName}: «{ShortName(item.NameLink)}» → этап «{WorkflowStages.Name(7)}»"
                : $"{actor.UserName}: «{ShortName(item.NameLink)}» — этап 5 завершён, ждём закрытия контракта",
            cancellationToken);

        _logger.LogInformation("Воркфлоу: закупка {Id} — этап 5 завершён, перевод на 7: {Moved} ({User})",
            regeditId, moved, actor.UserName);
        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> GetFieldColorsAsync(IEnumerable<int> regeditIds, CancellationToken cancellationToken = default)
    {
        var ids = regeditIds.Distinct().ToList();
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
            map[FieldColorKey(row.RegeditId, row.ColumnName)] = row.ColorCode;
        }

        return map;
    }

    /// <inheritdoc/>
    public async Task<Result> SetFieldColorAsync(int regeditId, string fieldKey, string? colorCode, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fieldKey))
        {
            return Result.Fail("Не указано поле.");
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var existing = await context.CellColors
            .FirstOrDefaultAsync(c => c.RegeditId == regeditId && c.ColumnName == fieldKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(colorCode))
        {
            if (existing is not null)
            {
                context.CellColors.Remove(existing);
            }
        }
        else if (existing is null)
        {
            context.CellColors.Add(new CellColor { RegeditId = regeditId, ColumnName = fieldKey, ColorCode = colorCode });
        }
        else
        {
            existing.ColorCode = colorCode;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить подсветку поля {Field} у закупки {RegeditId}", fieldKey, regeditId);
            return Result.Fail("Не удалось сохранить подсветку: " + ErrorText.Describe(ex));
        }

        _logger.LogInformation("Воркфлоу: подсветка поля {Field} закупки {Id} = {Color} ({User})",
            fieldKey, regeditId, string.IsNullOrWhiteSpace(colorCode) ? "снята" : colorCode, actor.UserName);
        return Result.Ok();
    }

    /// <summary>Ключ подсветки поля в словаре: «идентификатор закупки|имя поля».</summary>
    public static string FieldColorKey(int regeditId, string fieldKey) => $"{regeditId}|{fieldKey}";

    /// <summary>
    /// Этапы 5 «Перемещение товара» и 6 «Контракт» идут параллельно. На этап 7 закупку
    /// переводит тот, кто завершил свой этап последним: метод вызывается из обоих мест
    /// и срабатывает, только когда закрыты оба этапа.
    /// </summary>
    private static bool TryMoveToLogistics(Regedit item, RegistryDbContext context, ChangeActor actor, DateTime now)
    {
        if (item.Stage != 5 || item.Stage5ClosedAtUtc is null || item.ContractClosedAtUtc is null)
        {
            return false;
        }

        StageTracker.MoveTo(item, 7, actor.UserName);
        context.RegistryWorkflowLogs.Add(new RegistryWorkflowLog
        {
            RegeditId = item.Id,
            FromStage = 5,
            ToStage = 7,
            Action = JournalText.Action(WorkflowStages.Name(7)),
            ByUser = actor.UserName,
            AtUtc = now
        });
        return true;
    }

    public async Task<Result> SetStageAsync(int regeditId, int stage, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (!actor.CanChangeStage)
        {
            return Result.Fail(
                "Нет права переводить закупку на другую стадию. Право включается в карточке пользователя — «Менять стадию закупок».");
        }

        if (WorkflowStages.All.All(st => st.Id != stage))
        {
            return Result.Fail("Неизвестная стадия.");
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result.Fail("Запись не найдена.");
        }

        var from = item.Stage;
        if (from == stage)
        {
            return Result.Ok();
        }

        StageTracker.MoveTo(item, stage, actor.UserName);

        context.RegistryWorkflowLogs.Add(new RegistryWorkflowLog
        {
            RegeditId = regeditId,
            FromStage = from,
            ToStage = stage,
            // Подпись усекается: колонка [Action] ограничена, а два названия этапов в неё не влезают.
            Action = JournalText.Action($"Перевод на стадию вручную: {WorkflowStages.Name(from)} → {WorkflowStages.Name(stage)}"),
            Comment = JournalText.Comment($"«{WorkflowStages.Name(from)}» → «{WorkflowStages.Name(stage)}»"),
            ByUser = actor.UserName,
            AtUtc = DateTime.UtcNow
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить перевод закупки {RegeditId} на стадию {Stage}", regeditId, stage);
            return Result.Fail("Не удалось перенести закупку на стадию: " + ErrorText.Describe(ex));
        }

        // Ответственные за новую стадию получают уведомление, как при обычном переходе.
        await NotifyStageOwnersAsync(stage, item, cancellationToken);

        await NotifyAsync(regeditId, actor.UserName,
            $"{actor.UserName}: «{ShortName(item.NameLink)}» переведена вручную на «{WorkflowStages.Name(stage)}» (была «{WorkflowStages.Name(from)}»)",
            cancellationToken);

        _logger.LogInformation("Воркфлоу: закупка {Id} вручную {From}->{To} ({User})",
            regeditId, from, stage, actor.UserName);
        return Result.Ok();
    }

    public async Task<Result<string>> SaveBranchFileAsync(int regeditId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result<string>.Fail("Запись не найдена.");
        }

        // Path.GetFileName отрезает любой путь — имя файла нельзя использовать,
        // чтобы писать мимо таблицы вложений (../ и абсолютные пути).
        var safeName = Path.GetFileName(fileName);

        // Тип файла проверяется на сервере, а не только подсказкой accept в браузере:
        // запрос можно отправить напрямую, минуя страницу.
        if (safeName.Length == 0 || safeName.Any(char.IsControl) || !AttachmentLimits.IsAllowed(safeName))
        {
            return Result<string>.Fail(AttachmentLimits.NotAllowedMessage(safeName));
        }
        var (bytes, sizeError) = await AttachmentLimits.ReadAtMostAsync(content, safeName, cancellationToken);
        if (bytes is null)
        {
            return Result<string>.Fail(sizeError ?? AttachmentLimits.TooBigMessage(safeName, 0));
        }

        var file = new AttachmentFile
        {
            RegeditId = regeditId,
            FileName = safeName,
            ContentType = ContentTypeFromName(safeName),
            SizeBytes = bytes.Length,
            Content = bytes,
            UploadedAtUtc = DateTime.UtcNow
        };
        context.Attachments.Add(file);
        await context.SaveChangesAsync(cancellationToken); // получаем file.Id

        item.BranchFilePath = $"/attachments/{file.Id}/{safeName}";
        await context.SaveChangesAsync(cancellationToken);

        await NotifyAsync(regeditId, "Пользователь",
            $"Прикреплён файл для оповещения филиала: {safeName} (закупка «{ShortName(item.NameLink)}»)",
            cancellationToken);

        return Result<string>.Ok(item.BranchFilePath);
    }

    public async Task<Result> NotifyBranchAsync(int regeditId, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result.Fail("Запись не найдена.");
        }

        if (item.BranchId is null)
        {
            return Result.Fail("Выберите филиал для оповещения.");
        }

        var branch = await context.Branches.FirstOrDefaultAsync(b => b.Id == item.BranchId, cancellationToken);
        var branchName = branch?.Name ?? "Филиал";
        var fileLink = string.IsNullOrWhiteSpace(item.BranchFilePath) ? "(файл не прикреплён)" : item.BranchFilePath;

        var title = $"Оповещение филиала: {branchName}";
        var body =
            $"{item.NameLink}\n" +
            $"Заказчик: {item.Customer}\n" +
            $"Филиал: {branchName}\n" +
            $"Ожидаемая дата доставки: {item.ExpectedDeliveryDate:dd.MM.yyyy}\n" +
            $"Файл: {fileLink}\n" +
            $"Запись #{item.Id}";

        // Уведомление в Битрикс24: получателю филиала, иначе — общая рассылка (toUserId = null).
        await _bitrix.NotifyAsync(title, body, cancellationToken, branch?.BitrixUserId);

        // Перевод на следующий этап: 7 «Логистические документы» -> 8 «Ожидание доставки».
        var from = item.Stage;
        StageTracker.MoveTo(item, 8, actor.UserName);
        context.RegistryWorkflowLogs.Add(new RegistryWorkflowLog
        {
            RegeditId = regeditId,
            FromStage = from,
            ToStage = 8,
            Action = JournalText.Action("Оповещён филиал " + branchName),
            Comment = JournalText.Comment(branchName),
            ByUser = actor.UserName,
            AtUtc = DateTime.UtcNow
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить оповещение филиала {Branch} по закупке {RegeditId}", branchName, regeditId);
            return Result.Fail("Не удалось оповестить филиал: " + ErrorText.Describe(ex));
        }

        _logger.LogInformation("Воркфлоу: оповещён филиал {Branch} по закупке {Id} ({User})", branchName, regeditId, actor.UserName);

        await NotifyAsync(regeditId, actor.UserName,
            $"{actor.UserName}: оповещён филиал «{branchName}», «{ShortName(item.NameLink)}» → «{WorkflowStages.Name(8)}»",
            cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> ReturnAsync(int regeditId, string reason, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Fail("Укажите причину возврата.");
        }

        // ReturnReason в базе — nvarchar(2000); длинный текст SQL Server отклонит
        // ошибкой «String or binary data would be truncated».
        if (reason.Length > 2000)
        {
            return Result.Fail("Причина не должна быть длиннее 2000 символов (сейчас " + reason.Length + ").");
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result.Fail("Запись не найдена.");
        }

        var from = item.Stage;
        StageTracker.MoveTo(item, 13, actor.UserName);   // «Проблемы с приемкой»
        item.ReturnReason = reason.Trim();

        context.RegistryWorkflowLogs.Add(new RegistryWorkflowLog
        {
            RegeditId = regeditId,
            FromStage = from,
            ToStage = 13,
            Action = JournalText.Action("Проблема с приемкой: " + reason.Trim()),
            Comment = JournalText.Comment(reason),
            ByUser = actor.UserName,
            AtUtc = DateTime.UtcNow
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить возврат закупки {RegeditId}", regeditId);
            return Result.Fail("Не удалось оформить проблему с приемкой: " + ErrorText.Describe(ex));
        }

        _logger.LogInformation("Воркфлоу: возврат закупки {Id} ({User}): {Reason}", regeditId, actor.UserName, reason);

        await NotifyAsync(regeditId, actor.UserName,
            $"{actor.UserName}: «{ShortName(item.NameLink)}» → «{WorkflowStages.Name(13)}»",
            cancellationToken);

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

        // Path.GetFileName отрезает любой путь — имя файла нельзя использовать,
        // чтобы писать мимо таблицы вложений (../ и абсолютные пути).
        var safeName = Path.GetFileName(fileName);

        // Тип файла проверяется на сервере, а не только подсказкой accept в браузере:
        // запрос можно отправить напрямую, минуя страницу.
        if (safeName.Length == 0 || safeName.Any(char.IsControl) || !AttachmentLimits.IsAllowed(safeName))
        {
            return Result<string>.Fail(AttachmentLimits.NotAllowedMessage(safeName));
        }

        // Содержимое сохраняем в БД (таблица [Attachments]), а не в wwwroot — файлы не зависят от IIS.
        // Размер проверяется на сервере, а не только в браузере: большой файл не вычитывается до конца.
        var (bytes, sizeError) = await AttachmentLimits.ReadAtMostAsync(content, safeName, cancellationToken);
        if (bytes is null)
        {
            return Result<string>.Fail(sizeError ?? AttachmentLimits.TooBigMessage(safeName, 0));
        }

        var file = new AttachmentFile
        {
            RegeditId = regeditId,
            FileName = safeName,
            ContentType = ContentTypeFromName(safeName),
            SizeBytes = bytes.Length,
            Content = bytes,
            UploadedAtUtc = DateTime.UtcNow
        };
        context.Attachments.Add(file);
        await context.SaveChangesAsync(cancellationToken); // получаем file.Id

        var url = $"/attachments/{file.Id}/{safeName}";
        var list = string.IsNullOrWhiteSpace(item.Attachments)
            ? url
            : item.Attachments + ";" + url;
        item.Attachments = list;
        await context.SaveChangesAsync(cancellationToken);

        await NotifyAsync(regeditId, "Пользователь",
            $"Добавлено вложение: {safeName} (закупка «{ShortName(item.NameLink)}»)", cancellationToken);

        return Result<string>.Ok(list);
    }

    private static string ContentTypeFromName(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".zip" => "application/zip",
        ".rar" => "application/vnd.rar",
        ".7z" => "application/x-7z-compressed",
        ".json" => "application/json",
        ".xml" => "application/xml",
        _ => "application/octet-stream"
    };

    private static int? ParseAttachmentId(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var m = System.Text.RegularExpressions.Regex.Match(path, @"/attachments/(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    public async Task<Result> DeleteAttachmentAsync(int regeditId, string attachmentPath, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result.Fail("Запись не найдена.");
        }

        if (ParseAttachmentId(attachmentPath) is int fileId)
        {
            // Новое вложение в БД. Строку помечаем на удаление по первичному ключу,
            // НЕ загружая её: FirstOrDefaultAsync вычитывал в память весь файл (до 20 МБ)
            // только ради удаления — отсюда и медленное удаление.
            if (await context.Attachments.AnyAsync(a => a.Id == fileId, cancellationToken))
            {
                context.Entry(new AttachmentFile { Id = fileId }).State = EntityState.Deleted;
            }
        }
        else
        {
            // Старое вложение на файловой системе — удаляем физический файл.
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
        }

        var parts = (item.Attachments ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != attachmentPath)
            .ToList();
        item.Attachments = string.Join(";", parts);

        await context.SaveChangesAsync(cancellationToken);

        await NotifyAsync(regeditId, "Пользователь",
            $"Удалено вложение (закупка «{ShortName(item.NameLink)}»)", cancellationToken);

        return Result.Ok();
    }

    public async Task<(int Tomorrow, int Today)> GetUpcomingBiddingCountsAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var dates = await context.Regedit.AsNoTracking()
            .Where(r => r.BiddingDate != null)
            .Select(r => r.BiddingDate!.Value)
            .ToListAsync(cancellationToken);
        return (dates.Count(d => d.Date == tomorrow), dates.Count(d => d.Date == today));
    }
}
