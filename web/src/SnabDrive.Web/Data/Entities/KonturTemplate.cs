namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// «Контур ловушка» (выгрузка --templates) — отдельная таблица для директора.
/// Строки обновляются при повторных выгрузках (начало/конец дня): например,
/// «Метка» может быть пустой утром и заполниться («Интересно») к вечеру.
/// Хранит даты для будущей аналитики по периодам.
/// </summary>
public class KonturTemplate
{
    public int Id { get; set; }

    public string PurchaseNumber { get; set; } = string.Empty;
    public string NameLink { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public decimal NMCK { get; set; }

    public string? PlaceOfDelivery { get; set; }
    public string? Winner { get; set; }
    public decimal ResultPrice { get; set; }

    /// <summary>Метка из Контур — редактируется и дообновляется при повторных выгрузках.</summary>
    public string? Label { get; set; }

    public string? EisLink { get; set; }

    public DateTime? BiddingDate { get; set; }
    public DateTime? DateOfPlacement { get; set; }

    public string RawJson { get; set; } = string.Empty;

    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public string AddedBy { get; set; } = string.Empty;
}
