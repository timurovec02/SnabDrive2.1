namespace SnabDrive.Web.Data.Entities;

/// <summary>Вид операции в журнале изменений.</summary>
public static class AuditAction
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
    public const string Archived = "Archived";
    public const string Restored = "Restored";
    public const string CellColorChanged = "CellColorChanged";
    public const string DictionaryChanged = "DictionaryChanged";
    public const string UserChanged = "UserChanged";
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
}

/// <summary>
/// Запись журнала аудита. Новая таблица [AuditLog] — создаётся миграцией веб-приложения
/// и не затрагивает существующие таблицы реестра.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Одно из значений <see cref="AuditAction"/>.</summary>
    public string Action { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public int EntityId { get; set; }

    public string? UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    /// <summary>Сводка для ленты событий: «Иванов изменил запись №42».</summary>
    public string? Summary { get; set; }

    /// <summary>JSON со списком изменённых полей: {"Customer":{"old":"А","new":"Б"}}.</summary>
    public string? ChangesJson { get; set; }
}
