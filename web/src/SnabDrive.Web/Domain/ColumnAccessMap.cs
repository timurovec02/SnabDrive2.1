namespace SnabDrive.Web.Domain;

/// <summary>Режим доступа пользователя к колонкам реестра.</summary>
public enum ColumnAccessMode
{
    /// <summary>Все колонки видны и доступны для редактирования (поведение по умолчанию).</summary>
    AllColumns = 0,

    /// <summary>Только колонки, перечисленные в UserColumnPermission.</summary>
    CustomColumns = 1
}

/// <summary>Право на одну колонку.</summary>
public sealed record ColumnPermission(string Key, bool CanView, bool CanEdit)
{
    /// <summary>Право на просмотр подразумевает, что колонка хотя бы видна.</summary>
    public ColumnPermission Normalized() => new(Key, CanView, CanView && CanEdit);
}

/// <summary>
/// Эффективная карта доступа пользователя к колонкам реестра.
/// Применяется и в UI, и на сервере: API не отдаст скрытые значения и не примет
/// изменения полей, которые пользователю править нельзя.
/// </summary>
public sealed class ColumnAccessMap
{
    public static readonly ColumnAccessMap Full = new() { Unrestricted = true };

    /// <summary>true — ограничений нет (режим AllColumns или администратор).</summary>
    public bool Unrestricted { get; init; }

    public IReadOnlySet<string> Viewable { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Editable { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool CanView(string columnKey) => Unrestricted || Viewable.Contains(columnKey);

    public bool CanEdit(string columnKey) => Unrestricted || Editable.Contains(columnKey);

    public IReadOnlyList<RegistryColumn> Filter(IReadOnlyList<RegistryColumn> columns) =>
        Unrestricted ? columns : columns.Where(c => Viewable.Contains(c.Key)).ToList();

    public static ColumnAccessMap Build(IReadOnlyList<ColumnPermission> permissions)
    {
        var normalized = permissions.Select(p => p.Normalized()).ToList();

        return new ColumnAccessMap
        {
            Unrestricted = false,
            Viewable = normalized.Where(p => p.CanView).Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Editable = normalized.Where(p => p.CanEdit).Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
    }
}
