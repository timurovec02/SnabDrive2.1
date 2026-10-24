namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// Вложенный файл закупки. Содержимое хранится в БД (varbinary(max)), а не в wwwroot,
/// чтобы файлы не зависели от файловой системы IIS и переносились вместе с базой.
/// Таблица [Attachments].
/// </summary>
public class AttachmentFile
{
    public int Id { get; set; }

    /// <summary>Id записи реестра (RegistryItems.Id).</summary>
    public int RegeditId { get; set; }

    /// <summary>Исходное имя файла.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME-тип (для отдачи браузеру).</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Размер в байтах.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Двоичное содержимое файла.</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    public string UploadedBy { get; set; } = string.Empty;
}
