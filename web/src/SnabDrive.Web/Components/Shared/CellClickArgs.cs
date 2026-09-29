namespace SnabDrive.Web.Components.Shared;

/// <summary>Аргументы клика по ячейке таблицы (нужно для ручной подсветки).</summary>
public sealed record CellClickArgs(int RecordId, string ColumnKey, Domain.RegistryRowDto Row);

/// <summary>Результат инлайн-правки ячейки.</summary>
public sealed record CellEditCommit(int RecordId, string ColumnKey, string RawValue);
