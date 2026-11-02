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
