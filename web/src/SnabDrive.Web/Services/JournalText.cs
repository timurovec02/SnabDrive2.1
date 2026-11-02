using SnabDrive.Web.Data.Entities;

namespace SnabDrive.Web.Services;

/// <summary>
/// Подготовка текста для журнала <c>WorkflowLogs</c>.
/// Колонка [Action] ограничена по длине, и SQL Server отклоняет длинное значение
/// ошибкой «String or binary data would be truncated» — из-за этого не сохранялся
/// ручной перенос стадии во вкладке «Задачи» (в подписи два длинных названия этапов).
/// Поэтому подпись усекается здесь, до записи в базу, а подробности целиком
/// сохраняются в [Comment] (nvarchar(max)).
/// </summary>
public static class JournalText
{
    /// <summary>Предел длины подписи — тот же, что в маппинге и в базе.</summary>
    public const int ActionMaxLength = RegistryWorkflowLog.ActionMaxLength;

    /// <summary>Подпись события журнала: не длиннее <see cref="ActionMaxLength"/> символов.</summary>
    public static string Action(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length <= ActionMaxLength)
        {
            return value;
        }

        // Обрезаем на границе слова, чтобы в журнале не висел обрывок.
        var cut = value.Substring(0, ActionMaxLength - 1);
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > ActionMaxLength / 2)
        {
            cut = cut.Substring(0, lastSpace);
        }

        return cut.TrimEnd(' ', ',', ':', ';') + "…";
    }

    /// <summary>Подробности события — без ограничения длины (колонка nvarchar(max)).</summary>
    public static string? Comment(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length == 0 ? null : value;
    }
}
