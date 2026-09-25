using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SnabDrive.Web.Domain;
using SnabDrive.Web.Hubs;

namespace SnabDrive.Web.Services;

/// <summary>
/// Публикует событие изменения данных:
/// 1) во внутрипроцессную шину — по ней обновляются таблицы в Blazor Server UI (мгновенно);
/// 2) всем клиентам SignalR-хаба /hubs/registry — для внешних потребителей API и JS-виджетов.
/// </summary>
public sealed class SignalRRegistryNotifier : IRegistryNotifier
{
    public const string ChangeMethodName = "registryChanged";

    private readonly IRegistryEventBus _eventBus;
    private readonly IHubContext<RegistryHub> _hubContext;
    private readonly ILogger<SignalRRegistryNotifier> _logger;

    public SignalRRegistryNotifier(
        IRegistryEventBus eventBus,
        IHubContext<RegistryHub> hubContext,
        ILogger<SignalRRegistryNotifier> logger)
    {
        _eventBus = eventBus;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PublishAsync(RegistryChangeEvent changeEvent, CancellationToken cancellationToken = default)
    {
        _eventBus.Publish(changeEvent);

        try
        {
            await _hubContext.Clients.All.SendAsync(ChangeMethodName, changeEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            // SignalR-подписчиков может не быть — это не ошибка для основного сценария.
            _logger.LogDebug(ex, "Не удалось разослать событие {Kind} через SignalR", changeEvent.Kind);
        }
    }
}
