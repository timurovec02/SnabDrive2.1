namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// Права роли: доступ к вкладкам и права на колонки реестра.
/// Хранится одним рядом на роль; наборы сериализуются в JSON.
/// Таблица [RolePermissions].
/// </summary>
public class RolePermission
{
    /// <summary>Имя роли (AppRoles.*). Первичный ключ.</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>JSON: Dictionary&lt;string,bool&gt; — доступ к вкладкам (ключ AppFeatures → разрешено).</summary>
    public string FeaturesJson { get; set; } = "{}";

    /// <summary>JSON: List&lt;ColumnPermission&gt; — права на колонки. "[]" или null = все колонки.</summary>
    public string? ColumnsJson { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
