namespace SnabDrive.Web.Domain;

/// <summary>Строка журнала аудита для UI/API.</summary>
public sealed class AuditLogDto
{
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public IReadOnlyDictionary<string, FieldChange> Changes { get; set; }
        = new Dictionary<string, FieldChange>();

    public string CreatedAtLocal => CreatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
}

/// <summary>Изменение одного поля: было / стало.</summary>
public sealed record FieldChange(string? Old, string? New);

/// <summary>Параметры выборки журнала аудита.</summary>
public sealed record AuditQuery
{
    public string? EntityName { get; init; }
    public string? Action { get; init; }
    public string? UserName { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
