using Microsoft.AspNetCore.Identity;

namespace SnabDrive.Web.Data;

/// <summary>Пользователь веб-приложения (ASP.NET Core Identity).</summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Отображаемое имя (ФИО). Если не задано — используется UserName.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Id в старой таблице [User], если учётка импортирована из WPF-версии.</summary>
    public int? LegacyUserId { get; set; }

    public DateTime? LastLoginUtc { get; set; }

    public DateTime? LastSeenUtc { get; set; }

    /// <summary>Учётная запись заблокирована администратором (без удаления).</summary>
    public bool IsBlocked { get; set; }

    public string DisplayNameOrDefault => string.IsNullOrWhiteSpace(DisplayName) ? (UserName ?? string.Empty) : DisplayName;
}
