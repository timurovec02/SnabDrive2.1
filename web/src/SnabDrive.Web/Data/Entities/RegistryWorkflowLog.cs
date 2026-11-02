namespace SnabDrive.Web.Data.Entities;

/// <summary>Журнал передач задачи по стадиям воркфлоу (для истории и аудита).</summary>
public class RegistryWorkflowLog
{
    /// <summary>
    /// Длина колонки [Action] в базе (nvarchar). Держать в sync с updates_2026.sql.
    /// Длинную подпись SQL Server отклоняет ошибкой «String or binary data would be
    /// truncated», поэтому текст усекается до записи — см. JournalText.Action.
    /// </summary>
    public const int ActionMaxLength = 200;

    public int Id { get; set; }
    public int RegeditId { get; set; }
    public int FromStage { get; set; }
    public int ToStage { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string ByUser { get; set; } = string.Empty;
}
