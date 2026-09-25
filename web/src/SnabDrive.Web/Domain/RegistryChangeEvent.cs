namespace SnabDrive.Web.Domain;

/// <summary>Тип изменения для realtime-уведомлений.</summary>
public enum ChangeKind
{
    Created,
    Updated,
    Deleted,
    Archived,
    Restored,
    CellColorChanged,
    DictionaryChanged,
    UserChanged
}

/// <summary>Событие изменения данных, которое уходит всем подключённым клиентам.</summary>
public sealed record RegistryChangeEvent(
    ChangeKind Kind,
    string EntityName,
    int EntityId,
    string UserName,
    DateTime TimestampUtc,
    string? Summary = null,
    RegistryRowDto? Row = null)
{
    public string HumanText => Summary ?? DefaultSummary();

    private string DefaultSummary() => Kind switch
    {
        ChangeKind.Created => $"{UserName}: добавлена запись",
        ChangeKind.Updated => $"{UserName}: изменена запись",
        ChangeKind.Deleted => $"{UserName}: удалена запись",
        ChangeKind.Archived => $"{UserName}: запись перемещена в архив",
        ChangeKind.Restored => $"{UserName}: запись возвращена из архива",
        ChangeKind.CellColorChanged => $"{UserName}: изменена подсветка ячейки",
        ChangeKind.DictionaryChanged => $"{UserName}: изменён справочник",
        ChangeKind.UserChanged => $"{UserName}: изменены пользователи",
        _ => $"{UserName}: изменены данные"
    };
}
