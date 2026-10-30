namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// Филиал (справочник). Используется на этапе 7 «Логистические документы»:
/// пользователь выбирает филиал для оповещения в Битрикс24.
/// </summary>
public class Branch
{
    public int Id { get; set; }

    /// <summary>Название филиала.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Идентификатор пользователя/чата Битрикс24 для оповещения (необязательно).</summary>
    public string? BitrixUserId { get; set; }

    public bool IsActive { get; set; } = true;
}
