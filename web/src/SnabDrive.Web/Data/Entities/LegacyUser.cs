namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// Старая таблица пользователей WPF-клиента ([User]). Используется только для чтения —
/// однократного импорта учётных записей в ASP.NET Core Identity.
/// В веб-версии авторизация идёт через Identity, пароли в этой таблице больше не проверяются.
/// </summary>
public class LegacyUser
{
    public int Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
