namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// Факт отправленного уведомления о торгах (чтобы не спамить повторно).
/// Kind: "DayBefore" (за 1 день до торгов) или "DayOf" (в день торгов).
/// Таблица [BiddingNotifications].
/// </summary>
public class BiddingNotification
{
    public int Id { get; set; }

    /// <summary>Id записи реестра (закупки).</summary>
    public int RegeditId { get; set; }

    /// <summary>"DayBefore" или "DayOf".</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTime NotifiedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Сколько получателям отправлено (для контроля).</summary>
    public int RecipientsCount { get; set; }
}
