namespace SnabDrive.Web.Components.Shared;

/// <summary>Аргументы клика по ячейке таблицы (нужно для ручной подсветки).</summary>
public sealed record CellClickArgs(int RecordId, string ColumnKey, Domain.RegistryRowDto Row);
