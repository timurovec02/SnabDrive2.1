using Microsoft.AspNetCore.Identity;
using SnabDrive.Web.Data;

namespace SnabDrive.Web.Services;

/// <summary>
/// Состояние сессии конкретного Blazor-циркуита: идентификатор для учёта «онлайн»
/// и данные пользователя, полученные один раз при старте страницы.
/// Зарегистрирован как Scoped — по одному экземпляру на подключение пользователя.
/// </summary>
public sealed class UserSessionState
{
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public bool PresenceRegistered { get; set; }
}
