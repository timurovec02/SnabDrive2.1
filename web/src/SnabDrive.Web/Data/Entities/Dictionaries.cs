namespace SnabDrive.Web.Data.Entities;

/// <summary>Справочник «Статус резерва B2B». Таблица [B2BStatus], ключ — колонка [ID].</summary>
public class B2BStatus
{
    public int Id { get; set; }
    public string NameB2B { get; set; } = string.Empty;
}

/// <summary>Справочник «Тип закупки». Таблица [TypeOfPurchase], ключ — колонка [ID].</summary>
public class TypeOfPurchase
{
    public int Id { get; set; }
    public string NameOfPurchase { get; set; } = string.Empty;

    /// <summary>Цвет подсветки строки/ячейки в реестре (например #FF4CAF50).</summary>
    public string ColorCode { get; set; } = string.Empty;
}

/// <summary>Справочник «Статус исполнения контракта». Таблица [ExecutionStatus], ключ — колонка [ID].</summary>
public class ExecutionStatus
{
    public int Id { get; set; }
    public string NameExecution { get; set; } = string.Empty;
}
