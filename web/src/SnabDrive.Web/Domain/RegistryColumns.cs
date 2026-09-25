namespace SnabDrive.Web.Domain;

/// <summary>Описание колонки таблицы реестра.</summary>
/// <param name="Key">Имя свойства DTO — используется и как ключ сортировки/фильтра/цвета ячейки.</param>
/// <param name="Title">Заголовок в таблице.</param>
/// <param name="Kind">Тип значения — влияет на рендер, фильтр и формат экспорта.</param>
/// <param name="Width">CSS-ширина колонки.</param>
/// <param name="VisibleByDefault">Показывать ли колонку по умолчанию (настройка колонок).</param>
public sealed record RegistryColumn(string Key, string Title, ColumnKind Kind, string Width, bool VisibleByDefault = true)
{
    public bool IsText => Kind == ColumnKind.Text;
    public bool IsDate => Kind == ColumnKind.Date;
    public bool IsMoney => Kind == ColumnKind.Money;
}

public enum ColumnKind
{
    Text,
    Date,
    Money,
    Bool,
    Dictionary,
    Link
}

/// <summary>Набор колонок реестра — единый источник правды для UI, сортировки, фильтров и CSV-экспорта.</summary>
public static class RegistryColumns
{
    public static readonly IReadOnlyList<RegistryColumn> All = new List<RegistryColumn>
    {
        new("NameLink", "Ссылка / наименование", ColumnKind.Link, "260px"),
        new("Customer", "Заказчик", ColumnKind.Text, "180px"),
        new("TypeOfPurchaseId", "Тип закупки", ColumnKind.Dictionary, "160px"),
        new("PlaceOfDelivery", "Место поставки товара", ColumnKind.Text, "170px"),
        new("ReserveNumber", "Номер резерва", ColumnKind.Text, "150px"),
        new("NationalMode", "Нац режим", ColumnKind.Text, "120px"),
        new("BiddingDate", "Дата торгов", ColumnKind.Date, "130px"),
        new("DateOfTransferForPlacement", "Дата передачи на размещение", ColumnKind.Date, "150px"),
        new("DateOfPlacement", "Дата размещения на площадке", ColumnKind.Date, "150px"),
        new("DateResults", "Дата подведения итогов", ColumnKind.Date, "150px"),
        new("DateOfConclusionOfTheContract", "Дата заключения контракта", ColumnKind.Date, "150px"),
        new("NMCK", "НМЦК", ColumnKind.Money, "130px"),
        new("MinPrice", "Наша минимальная сумма", ColumnKind.Money, "150px"),
        new("ResultPrice", "Итоговая сумма", ColumnKind.Money, "140px"),
        new("Winner", "Победитель", ColumnKind.Text, "180px"),
        new("DeliveryTime", "Срок поставки", ColumnKind.Text, "120px"),
        new("Description", "Примечания", ColumnKind.Text, "180px", VisibleByDefault: false),
        new("Note", "Доп записка", ColumnKind.Text, "180px", VisibleByDefault: false),
        new("B2BStatusId", "Статус резерва B2B", ColumnKind.Dictionary, "170px"),
        new("ExecutionStatusId", "Статус исполнения контракта", ColumnKind.Dictionary, "180px"),
        new("IsFinished", "Оплачен", ColumnKind.Bool, "90px"),
        new("ArchivateDate", "Дата архивации", ColumnKind.Date, "140px", VisibleByDefault: false)
    };

    public static IEnumerable<RegistryColumn> DefaultVisible => All.Where(c => c.VisibleByDefault);

    public static RegistryColumn? Find(string key) =>
        All.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    public static bool IsKnown(string key) => Find(key) is not null;
}
