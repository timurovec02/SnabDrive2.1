using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SnabDrive.Web.Services;

namespace SnabDrive.Web.Hubs;

/// <summary>
/// SignalR-хаб реестра: уведомления об изменениях записей и учёт онлайн-пользователей.
///
/// События об изменениях приходят от сервера методом "registryChanged" (см. SignalRRegistryNotifier).
/// Список онлайн-пользователей доступен через "getOnlineUsers" и рассылается событием "presenceChanged".
/// </summary>
[Authorize]
public class RegistryHub : Hub
{
    public const string Route = "/hubs/registry";
    public const string PresenceChangedMethod = "presenceChanged";
    public const string GetOnlineUsersMethod = "getOnlineUsers";

    private readonly IPresenceService _presence;
    private readonly ILogger<RegistryHub> _logger;

    public RegistryHub(IPresenceService presence, ILogger<RegistryHub> logger)
    {
        _presence = presence;
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        var (userId, userName) = ReadUser();
        if (userId is not null)
        {
            _presence.Register(Context.ConnectionId, SessionKind.Hub, userId, userName);
            _logger.LogInformation("SignalR: подключился {User} ({ConnectionId})", userName, Context.ConnectionId);
        }

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _presence.Unregister(Context.ConnectionId);

        if (exception is not null)
        {
            _logger.LogDebug(exception, "SignalR: соединение {ConnectionId} закрыто с ошибкой", Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Проверка живости соединения + отметка активности.</summary>
    public Task Ping()
    {
        _presence.Touch(Context.ConnectionId);
        return Task.CompletedTask;
    }

    /// <summary>Актуальный список пользователей в системе.</summary>
    public Task<object> GetOnlineUsers() => Task.FromResult<object>(_presence.GetOnline());

    private (string? UserId, string UserName) ReadUser()
    {
        var principal = Context.User;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = principal?.Identity?.Name ?? "неизвестный";
        return (userId, userName);
    }
}
