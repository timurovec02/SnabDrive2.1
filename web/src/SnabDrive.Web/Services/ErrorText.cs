using System.Text;

namespace SnabDrive.Web.Services;

/// <summary>
/// Превращает исключение в текст, который можно показать пользователю.
/// EF при ошибке записи отдаёт общее «An error occurred while saving the entity
/// changes», а настоящая причина (например, «Invalid column name …» при
/// неприменённой миграции) лежит во внутренних исключениях.
/// </summary>
public static class ErrorText
{
    public static string Describe(Exception ex)
    {
        var chain = new List<string>();
        for (Exception? e = ex; e is not null && chain.Count < 5; e = e.InnerException)
        {
            var message = e.Message?.Trim() ?? string.Empty;
            if (message.Length > 0 && !chain.Contains(message, StringComparer.Ordinal))
            {
                chain.Add(message);
            }
        }

        var text = string.Join(" — ", chain);
        return NeedsMigration(text) ? text + MigrationHint : text;
    }

    private const string MigrationHint =
        " Похоже, в базе нет новой колонки: выполните на сервере скрипт web/sql/updates_2026.sql.";

    private static bool NeedsMigration(string text) =>
        text.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Недопустимое имя столбца", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Недопустимое имя объекта", StringComparison.OrdinalIgnoreCase);

    /// <summary>Короткая сводка для журнала: тип исключения и причина.</summary>
    public static string Brief(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null)
        {
            inner = inner.InnerException;
        }

        return new StringBuilder(ex.GetType().Name).Append(": ").Append(inner.Message).ToString();
    }
}
