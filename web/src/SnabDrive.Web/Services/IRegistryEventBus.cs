using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

/// <summary>
/// Внутрипроцессная шина событий реестра. Blazor Server-компоненты подписываются на неё и
/// обновляют таблицы мгновенно, без перезагрузки страницы и без ручного «Обновить».
///
/// Те же события параллельно уходят внешним клиентам через SignalR (см. SignalRRegistryNotifier).
/// </summary>
public interface IRegistryEventBus
{
    event Action<RegistryChangeEvent>? Changed;

    event Action<IReadOnlyList<OnlineUser>>? PresenceChanged;

    void Publish(RegistryChangeEvent changeEvent);

    void PublishPresence(IReadOnlyList<OnlineUser> onlineUsers);
}

public sealed class RegistryEventBus : IRegistryEventBus
{
    private readonly ILogger<RegistryEventBus> _logger;

    public RegistryEventBus(ILogger<RegistryEventBus> logger)
    {
        _logger = logger;
    }

    public event Action<RegistryChangeEvent>? Changed;

    public event Action<IReadOnlyList<OnlineUser>>? PresenceChanged;

    public void Publish(RegistryChangeEvent changeEvent)
    {
        // Событие инициировано одним пользователем, а обработчики живут в чужих Blazor-циркулах.
        // Падение одного подписчика (например, вкладку уже закрыли) не должно ломать операцию инициатора.
        var handler = Changed;
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<RegistryChangeEvent>)subscriber).Invoke(changeEvent);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Подписчик шины событий отработал с ошибкой");
            }
        }
    }

    public void PublishPresence(IReadOnlyList<OnlineUser> onlineUsers)
    {
        var handler = PresenceChanged;
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<IReadOnlyList<OnlineUser>>)subscriber).Invoke(onlineUsers);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Подписчик списка онлайн отработал с ошибкой");
            }
        }
    }
}
