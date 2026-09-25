namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// Право пользователя на конкретную колонку реестра. Новая таблица [UserColumnPermission].
/// Учитывается только если у пользователя ApplicationUser.ColumnAccessMode = CustomColumns.
/// </summary>
public class UserColumnPermission
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>Ключ колонки — RegistryColumn.Key (NameLink, Customer, NMCK, …).</summary>
    public string ColumnKey { get; set; } = string.Empty;

    public bool CanView { get; set; }

    public bool CanEdit { get; set; }

    public virtual ApplicationUser? User { get; set; }
}
