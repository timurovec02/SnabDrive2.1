namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// Строка «Избранного Контур» — закупка, выгруженная скриптом из Контур.Закупки
/// и ожидающая просмотра. Пользователь отбирает нужные и переносит их в реестр.
/// </summary>
public class KonturFavorite
{
    public int Id { get; set; }

    /// <summary>Номер закупки (реестровый номер) — для контроля дублей.</summary>
    public string PurchaseNumber { get; set; } = string.Empty;

    /// <summary>Наименование / предмет закупки.</summary>
    public string NameLink { get; set; } = string.Empty;

    /// <summary>Заказчик.</summary>
    public string Customer { get; set; } = string.Empty;

    /// <summary>НМЦК.</summary>
    public decimal NMCK { get; set; }

    /// <summary>Место поставки; NULL для строк до ввода колонки.</summary>
    public string? PlaceOfDelivery { get; set; }

    /// <summary>Победитель / поставщик; NULL для строк до ввода колонки.</summary>
    public string? Winner { get; set; }

    /// <summary>Итоговая сумма (предложение победителя / цена договора).</summary>
    public decimal ResultPrice { get; set; }

    /// <summary>Метка из Контур (отображается как «Статус»); NULL для строк до ввода колонки.</summary>
    public string? Label { get; set; }

    /// <summary>Ссылка на ЕИС; NULL для строк до ввода колонки.</summary>
    public string? EisLink { get; set; }

    /// <summary>Дата окончания подачи заявок / торгов.</summary>
    public DateTime? BiddingDate { get; set; }

    /// <summary>Дата публикации (размещения).</summary>
    public DateTime? DateOfPlacement { get; set; }

    public KonturFavoriteStatus Status { get; set; } = KonturFavoriteStatus.New;

    /// <summary>Id записи в Regedit, если строка перенесена в реестр.</summary>
    public int? RegeditId { get; set; }

    /// <summary>Исходные колонки выгрузки (JSON) — для отладки и уточнения сопоставления.</summary>
    public string RawJson { get; set; } = string.Empty;

    public DateTime AddedAt { get; set; } = DateTime.Now;
    public string AddedBy { get; set; } = string.Empty;
}

public enum KonturFavoriteStatus
{
    New = 0,
    Imported = 1,
    Rejected = 2
}
