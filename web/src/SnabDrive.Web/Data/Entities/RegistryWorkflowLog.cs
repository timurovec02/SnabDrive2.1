namespace SnabDrive.Web.Data.Entities;

/// <summary>Журнал передач задачи по стадиям воркфлоу (для истории и аудита).</summary>
public class RegistryWorkflowLog
{
    public int Id { get; set; }
    public int RegeditId { get; set; }
    public int FromStage { get; set; }
    public int ToStage { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string ByUser { get; set; } = string.Empty;
}
