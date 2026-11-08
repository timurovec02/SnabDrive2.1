using System.Globalization;

namespace SnabDrive.Web.Services;

/// <summary>
/// Ограничение размера загружаемых файлов.
/// Предел объявлен в одном месте: его используют и страница (проверка до чтения
/// файла), и сервис (проверка при записи), поэтому они не могут разойтись.
/// </summary>
public static class AttachmentLimits
{
    /// <summary>Разрешённый размер в мегабайтах.</summary>
    public const int MaxSizeMb = 20;

    /// <summary>Разрешённый размер в байтах.</summary>
    public const long MaxFileSizeBytes = MaxSizeMb * 1024L * 1024L;

    public static bool IsTooBig(long sizeBytes) => sizeBytes > MaxFileSizeBytes;

    /// <summary>
    /// Разрешённые расширения загружаемых файлов.
    /// Список положительный («разрешено только это»), а не отрицательный: новый опасный
    /// тип не попадёт в систему просто потому, что его никто не разрешил.
    ///
    /// Намеренно НЕ разрешены: исполняемые файлы (.exe, .dll, .bat, .cmd, .scr, .msi, .ps1,
    /// .vbs, .js, .jar, .hta, .lnk, .reg), веб-сценарии (.asp, .aspx, .php, .jsp, .cgi)
    /// и разметка (.html, .htm, .svg, .xml) — последние используются для хранения чужого
    /// скрипта, который браузер выполнил бы при открытии файла с нашего домена.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".rtf", ".odt", ".xls", ".xlsx", ".ods", ".ppt", ".pptx", ".odp",
        ".csv", ".txt", ".json", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp",
        ".zip", ".rar", ".7z", ".msg", ".eml"
    };

    /// <summary>Значение атрибута accept у InputFile — подсказка браузеру (проверка всё равно на сервере).</summary>
    public static string AcceptAttribute => string.Join(',', AllowedExtensions.OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Расширение файла в нижнем регистре; пусто, если расширения нет.</summary>
    public static string ExtensionOf(string fileName) => Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

    /// <summary>Проверка типа файла по расширению. Файлы без расширения не принимаем.</summary>
    public static bool IsAllowed(string fileName)
    {
        var extension = ExtensionOf(fileName);
        return extension.Length > 0 && AllowedExtensions.Contains(extension);
    }

    /// <summary>Сообщение об отказе по типу файла — на русском.</summary>
    public static string NotAllowedMessage(string fileName)
    {
        var extension = ExtensionOf(fileName);
        var shown = extension.Length > 0 ? extension : "без расширения";
        return $"Файл «{fileName}» ({shown}) — такой тип загружать нельзя. " +
               $"Разрешены документы, таблицы, изображения и архивы: {string.Join(", ", AllowedExtensions.OrderBy(x => x, StringComparer.Ordinal))}.";
    }

    /// <summary>Человекочитаемый размер: «850 КБ», «27,4 МБ».</summary>
    public static string Size(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString("0", CultureInfo.InvariantCulture) + " Б";
        }

        return bytes < 1024L * 1024L
            ? (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " КБ"
            : (bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture) + " МБ";
    }

    /// <summary>Сообщение об отказе — на русском, в отличие от исключения Blazor.</summary>
    public static string TooBigMessage(string fileName, long sizeBytes) =>
        $"Файл «{fileName}» — {Size(sizeBytes)}; разрешено не более {MaxSizeMb} МБ.";

    /// <summary>Подсказка рядом с полем выбора файла.</summary>
    public static string Hint => $"до {MaxSizeMb} МБ";

    /// <summary>
    /// Подсказка для атрибута title у InputFile. Отдельное свойство нужно потому,
    /// что атрибуты компонентов Razor не поддерживают смесь текста и выражения
    /// (ошибка RZ9986) — значение должно быть одним C#-выражением.
    /// </summary>
    public static string FileHint => $"Файл {Hint}";

    /// <summary>
    /// Читает поток целиком, но останавливается сразу после превышения предела —
    /// большой файл не вычитывается в память до конца.
    /// </summary>
    public static async Task<(byte[]? Bytes, string? Error)> ReadAtMostAsync(
        Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        // Поток из Blazor знает свою длину заранее — отказываем, не читая ни байта.
        if (content.CanSeek && IsTooBig(content.Length))
        {
            return (null, TooBigMessage(fileName, content.Length));
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (IsTooBig(total))
            {
                return (null, TooBigMessage(fileName, total));
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), null);
    }
}
