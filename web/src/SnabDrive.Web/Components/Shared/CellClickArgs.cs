namespace SnabDrive.Web.Components.Shared;

/// <summary>Аргументы клика по ячейке таблицы (нужно для ручной подсветки).</summary>
public sealed record CellClickArgs(int RecordId, string ColumnKey, Domain.RegistryRowDto Row);

/// <summary>ПКМ по ячейке: что кликнули и координаты курсора для контекстного меню подсветки.</summary>
public sealed record CellMenuArgs(CellClickArgs Cell, int X, int Y);

/// <summary>Результат инлайн-правки ячейки.</summary>
public sealed record CellEditCommit(int RecordId, string ColumnKey, string RawValue);
