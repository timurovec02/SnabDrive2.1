using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace KonturExport;

internal sealed record OutputResult(string Path, long Bytes, string Format);
internal sealed record RecognizedFormat(string Extension, string Description);

internal static class OutputFiles
{
    public static async Task<OutputResult> SaveAsync(IDownload download, Settings settings, RunLog log)
    {
        Directory.CreateDirectory(settings.OutputDirectory);
        string suffix = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..6]}";
        string stem = SafeStem(settings.FileNamePrefix) + "_" + suffix;
        string temporary = Path.Combine(settings.OutputDirectory, stem + ".part");
        TimeSpan limit = TimeSpan.FromSeconds(settings.TransferTimeoutSeconds);
        try
        {
            string? failure = await download.FailureAsync().WaitAsync(limit);
            if (failure != null) throw new IOException("Скачивание не завершилось: " + failure);
            log.Info($"SaveAsAsync -> {temporary}");
            await download.SaveAsAsync(temporary).WaitAsync(limit);
            var file = new FileInfo(temporary);
            if (!file.Exists || file.Length == 0) throw new IOException("Сохранённый файл отсутствует или пуст.");

            RecognizedFormat? format = DetectFormat(temporary, download.SuggestedFilename);
            if (format == null)
            {
                string diagnostic = Path.ChangeExtension(temporary, ".bin");
                File.Move(temporary, diagnostic);
                throw new InvalidDataException(
                    "Полученный файл не распознан как Excel/CSV. Не добавляем .xlsx вслепую. " +
                    "Возможно, сайт вернул страницу входа или ошибку. Файл для диагностики: " + diagnostic);
            }
            string destination = Path.Combine(settings.OutputDirectory, stem + format.Extension);
            File.Move(temporary, destination);
            return new OutputResult(Path.GetFullPath(destination), file.Length, format.Description);
        }
        catch (System.TimeoutException)
        {
            try { await download.CancelAsync(); } catch { }
            throw new System.TimeoutException($"Сайт начал файл, но передача/сохранение не закончились за {settings.TransferTimeoutSeconds} сек.");
        }
    }

    public static string SafeStem(string? value)
    {
        string stem = Regex.Replace(value ?? "", "[<>:\"/\\\\|?*\\x00-\\x1f]", "_").Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(stem)) stem = "Избранное";
        if (stem.Length > 80) stem = stem[..80];
        // Префикс и timestamp исключают зарезервированные Windows-имена.
        return stem;
    }

    public static RecognizedFormat? DetectFormat(string file, string suggestedName)
    {
        using var stream = File.OpenRead(file);
        byte[] header = new byte[8];
        int count = stream.Read(header, 0, header.Length);
        if (count >= 4 && header[0] == 0x50 && header[1] == 0x4b)
        {
            try
            {
                using var archive = ZipFile.OpenRead(file);
                var entries = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (entries.Contains("[Content_Types].xml") && entries.Contains("xl/workbook.xml"))
                {
                    bool macros = entries.Contains("xl/vbaProject.bin");
                    return new(macros ? ".xlsm" : ".xlsx", macros ? "Excel XLSM (Open XML)" : "Excel XLSX (Open XML)");
                }
                if (entries.Contains("[Content_Types].xml") && entries.Contains("xl/workbook.bin"))
                    return new(".xlsb", "Excel XLSB");
                return null; // Обычный ZIP нельзя выдавать за XLSX.
            }
            catch (InvalidDataException) { return null; }
        }
        byte[] ole = [0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1];
        if (count == 8 && header.SequenceEqual(ole))
        {
            // В контексте команды Excel это может быть старый XLS или защищённый XLSX.
            string native = Path.GetExtension(suggestedName).ToLowerInvariant();
            return new(native == ".xlsx" ? ".xlsx" : ".xls", "Excel / OLE-контейнер");
        }
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        char[] chars = new char[8192];
        string text = new(chars, 0, reader.Read(chars, 0, chars.Length));
        string trimmed = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        string lower = trimmed.ToLowerInvariant();
        if (lower.Contains("urn:schemas-microsoft-com:office:spreadsheet") ||
            (lower.Contains("urn:schemas-microsoft-com:office:excel") && lower.Contains("<table")))
            return new(".xls", "Excel legacy XML/HTML");
        if (lower.StartsWith("<!doctype") || lower.StartsWith("<html") || lower.StartsWith("<body") ||
            lower.StartsWith("{") || lower.StartsWith("[")) return null;
        string ext = Path.GetExtension(suggestedName).ToLowerInvariant();
        if ((ext == ".csv" || ext == ".tsv") && !text.Contains('\0') && !text.Contains('\uFFFD'))
            return new(ext, ext == ".csv" ? "CSV" : "TSV");
        return null;
    }
}
