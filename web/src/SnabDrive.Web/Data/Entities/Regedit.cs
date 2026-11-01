namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// Основная запись реестра закупок. Соответствует существующей таблице [Regedit] в SnabDriveDB.
/// Соответствует таблице [RegistryItems] в snabdrive_db.
/// </summary>
public class Regedit
{
    public int Id { get; set; }

    public string NameLink { get; set; } = string.Empty;

    /// <summary>Ссылка на закупку в ЕИС (отдельно от наименования).</summary>
    public string? EisLink { get; set; }

    /// <summary>Номер закупки (реестровый номер из Контур, поле «Номер»).</summary>
    public string PurchaseNumber { get; set; } = string.Empty;

    public string PlaceOfDelivery { get; set; } = string.Empty;
    public string ReserveNumber { get; set; } = string.Empty;
    public string NationalMode { get; set; } = string.Empty;

    public DateTime? DateOfTransferForPlacement { get; set; }
    public DateTime? DateOfPlacement { get; set; }
    public DateTime? BiddingDate { get; set; }
    public DateTime? DateResults { get; set; }
    public DateTime? DateOfConclusionOfTheContract { get; set; }

    public decimal NMCK { get; set; }
    public decimal MinPrice { get; set; }
    public decimal ResultPrice { get; set; }

    /// <summary>Дата доставки (бывший текстовый «Срок поставки»).</summary>
    public DateTime? DeliveryDate { get; set; }

    public string Description { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;

    public int? TypeOfPurchaseId { get; set; }
    public int? B2BStatusId { get; set; }
    public int? ExecutionStatusId { get; set; }

    public bool? IsFinished { get; set; }

    /// <summary>Отметка «Согласован» (используется на стадии согласования).</summary>
    public bool IsApproved { get; set; }

    /// <summary>Отметка «Звони» (иконка телефона) — подсветка мин. суммы на торгах.</summary>
    public bool IsCall { get; set; }

    /// <summary>Отметка «По заявкам» (этап 1).</summary>
    public bool IsByRequest { get; set; }

    /// <summary>Ожидаемая дата доставки (этапы 5 и 7).</summary>
    public DateTime? ExpectedDeliveryDate { get; set; }

    /// <summary>Выбранный филиал (этап 7, справочник Branches); NULL — не выбран.</summary>
    public int? BranchId { get; set; }

    /// <summary>Отдельный файл для оповещения филиала (этап 7).</summary>
    public string? BranchFilePath { get; set; }

    /// <summary>Причина возврата (этап 9 -> «Проблемы с приемкой»).</summary>
    public string? ReturnReason { get; set; }

    /// <summary>Статус контракта (этап 6): 1 — на подписании у Снабдрайв, 2 — на подписании у заказчика, 3 — подписан.</summary>
    public int? ContractStatusId { get; set; }

    /// <summary>Отметка «Проверен» (этап 6 — контракт).</summary>
    public bool IsContractChecked { get; set; }

    /// <summary>Отметка «Заполнить в ЕИС» (этап 7) — подсвечивает дату доставки на этапе 8.</summary>
    public bool IsEisFilled { get; set; }

    // ---- Воркфлоу по стадиям (web): 1 = поиск/подготовка, 2 = участие, 3 = оформление, 4 = завершено; IsLost = проигран ----
    public int Stage { get; set; } = 1;
    public bool IsLost { get; set; }

    /// <summary>Когда начался текущий этап (UTC).</summary>
    public DateTime? StageStartedAtUtc { get; set; }

    /// <summary>Кто начал текущий этап.</summary>
    public string? StageStartedBy { get; set; }

    /// <summary>Когда был завершён предыдущий этап (UTC).</summary>
    public DateTime? StageCompletedAtUtc { get; set; }

    /// <summary>Кто завершил предыдущий этап — для учёта работы каждого сотрудника.</summary>
    public string? StageCompletedBy { get; set; }
    public string? AssignedTo { get; set; }

    // Дополнительные поля воркфлоу
    public string? Place { get; set; }          // «Место» (стадия торгов)
    public string? Attachments { get; set; }    // имена сохранённых файлов (через ;)

    public virtual B2BStatus? B2BStatus { get; set; }
    public virtual TypeOfPurchase? TypeOfPurchase { get; set; }
    public virtual ExecutionStatus? ExecutionStatus { get; set; }
}
