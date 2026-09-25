using System.Collections.Concurrent;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

/// <summary>Источник сессии пользователя.</summary>
public enum SessionKind
{
    /// <summary>Blazor Server circuit (открытая вкладка браузера).</summary>
    Blazor,

    /// <summary>Подключение к SignalR-хабу.</summary>
    Hub
}

/// <summary>
/// Учёт активных пользователей: кто сейчас в системе и когда последний раз подавал признаки жизни.
/// Сессии приходят из двух мест: Blazor-circuit'ов (MainLayout) и SignalR-подключений (RegistryHub).
/// </summary>
public interface IPresenceService
{
    IReadOnlyList<OnlineUser> GetOnline();

    int OnlineCount { get; }

    void Register(string sessionId, SessionKind kind, string userId, string userName);

    void Unregister(string sessionId);

    void Touch(string sessionId);
}

public sealed class PresenceService : IPresenceService
{
    private sealed record Session(string SessionId, SessionKind Kind, string UserId, string UserName,
                                  DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc);

    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly IRegistryEventBus _eventBus;

    public PresenceService(IRegistryEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public int OnlineCount => _sessions.Values.Select(s => s.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public IReadOnlyList<OnlineUser> GetOnline()
    {
        return _sessions.Values
            .GroupBy(s => s.UserId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new OnlineUser(
                g.Key,
                g.Select(s => s.UserName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).First(),
                g.Min(s => s.FirstSeenUtc),
                g.Max(s => s.LastSeenUtc),
                g.Count()))
            .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Register(string sessionId, SessionKind kind, string userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _sessions.AddOrUpdate(
            sessionId,
            _ => new Session(sessionId, kind, userId, userName, now, now),
            (_, existing) => existing with { UserName = userName, LastSeenUtc = now });

        Broadcast();
    }

    public void Unregister(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (_sessions.TryRemove(sessionId, out _))
        {
            Broadcast();
        }
    }

    public void Touch(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            _sessions[sessionId] = existing with { LastSeenUtc = DateTimeOffset.UtcNow };
        }
    }

    private void Broadcast()
    {
        _eventBus.PublishPresence(GetOnline());
    }
}
