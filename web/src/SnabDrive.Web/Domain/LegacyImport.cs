namespace SnabDrive.Web.Domain;

/// <summary>
/// Одна строка старой выгрузки реестра, разобранная под поля сайта.
/// Модель живёт в Domain, чтобы её могли показать и Blazor-страница, и API.
/// </summary>
public sealed class LegacyImportRow
{
    /// <summary>Номер строки в файле — чтобы можно было вернуться к оригиналу.</summary>
    public int SourceRow { get; set; }

    public string NameLink { get; set; } = string.Empty;
    public string? EisLink { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string PlaceOfDelivery { get; set; } = string.Empty;
    public string ReserveNumber { get; set; } = string.Empty;
    public string NationalMode { get; set; } = string.Empty;

    /// <summary>«Срок поставки» из файла — текстовые сроки (в сайте поле DeliveryTerms).</summary>
    public string DeliveryTerms { get; set; } = string.Empty;

    /// <summary>«Примечания» в реестре.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>«Примечание» в карточке «Задач» (в базе — отдельная колонка Note).</summary>
    public string Note { get; set; } = string.Empty;

    public DateTime? DateOfTransferForPlacement { get; set; }
    public DateTime? DateOfPlacement { get; set; }
    public DateTime? BiddingDate { get; set; }
    public DateTime? DateResults { get; set; }
    public DateTime? DateOfConclusionOfTheContract { get; set; }

    public decimal NMCK { get; set; }
    public decimal MinPrice { get; set; }
    public decimal ResultPrice { get; set; }

    /// <summary>Значение «Тип закупки, закон» из файла — как есть, до сверки со справочником.</summary>
    public string TypeText { get; set; } = string.Empty;

    /// <summary>Значение «Статус резерва В2В» из файла.</summary>
    public string B2BText { get; set; } = string.Empty;

    public int? TypeOfPurchaseId { get; set; }
    public int? B2BStatusId { get; set; }

    public bool IsApproved { get; set; }
    public bool IsCall { get; set; }
    public bool IsByRequest { get; set; }

    /// <summary>Статус контракта: 3 — «Подписан» (из «Дата заключения контракта: подписан …»).</summary>
    public int? ContractStatusId { get; set; }

    /// <summary>Стадия, на которую встанет закупка (WorkflowStages).</summary>
    public int Stage { get; set; } = 1;

    /// <summary>Такая закупка уже есть в реестре — при импорте будет пропущена.</summary>
    public bool IsDuplicate { get; set; }

    /// <summary>Что не удалось разобрать в строке (показывается в предпросмотре).</summary>
    public List<string> Problems { get; } = new();
}

/// <summary>Строка статистики разбора: «поле — сколько значений перенесено».</summary>
public sealed record LegacyImportStat(string Title, int Count, string? Hint = null);

/// <summary>Результат разбора файла — то, что видит пользователь до импорта.</summary>
public sealed class LegacyImportPreview
{
    public string FileName { get; set; } = string.Empty;

    public List<LegacyImportRow> Rows { get; } = new();

    /// <summary>Колонки файла, которым нет места в сайте (не импортируются).</summary>
    public List<string> IgnoredColumns { get; } = new();

    /// <summary>Сколько значений легло в каждое поле.</summary>
    public List<LegacyImportStat> Stats { get; } = new();

    /// <summary>Значения, которых нет в справочнике «Тип закупки» (поле останется пустым).</summary>
    public List<LegacyImportStat> UnknownTypes { get; } = new();

    /// <summary>Значения, которых нет в справочнике «Статус резерва B2B».</summary>
    public List<LegacyImportStat> UnknownStatuses { get; } = new();

    /// <summary>Предупреждения разбора («дата не распознана» и т. п.).</summary>
    public List<LegacyImportStat> Warnings { get; } = new();

    /// <summary>Сколько закупок встанет на каждую стадию.</summary>
    public List<LegacyImportStat> Stages { get; } = new();

    public int NewRows => Rows.Count(row => !row.IsDuplicate);

    public int Duplicates => Rows.Count(row => row.IsDuplicate);
}

/// <summary>Итог импорта.</summary>
public sealed record LegacyImportReport(int Imported, int SkippedDuplicates);
