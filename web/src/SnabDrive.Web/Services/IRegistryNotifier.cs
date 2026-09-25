using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

/// <summary>Разносит событие изменения по всем каналам: Blazor-шины и SignalR.</summary>
public interface IRegistryNotifier
{
    Task PublishAsync(RegistryChangeEvent changeEvent, CancellationToken cancellationToken = default);
}
