namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// Ручная раскраска ячеек реестра. Таблица [CellColors].
/// <see cref="ColumnName"/> — имя свойства записи (Customer, PlaceOfDelivery, BiddingDate, ...),
/// точно как ConverterParameter в WPF-версии, поэтому старые цвета продолжают работать.
/// </summary>
public class CellColor
{
    public int Id { get; set; }

    public int RegeditId { get; set; }

    public string ColumnName { get; set; } = string.Empty;

    public string ColorCode { get; set; } = string.Empty;

    public virtual Regedit? Regedit { get; set; }
}
