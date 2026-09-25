using Microsoft.Extensions.Logging.Abstractions;
using SnabDrive.Web.Domain;
using SnabDrive.Web.Services;
using Xunit;

namespace SnabDrive.Web.Tests;

public class PresenceServiceTests
{
    private static (PresenceService Presence, RegistryEventBus Bus) Create()
    {
        var bus = new RegistryEventBus(NullLogger<RegistryEventBus>.Instance);
        return (new PresenceService(bus), bus);
    }

    [Fact]
    public void Register_добавляет_пользователя_в_онлайн()
    {
        var (presence, _) = Create();

        presence.Register("session-1", SessionKind.Blazor, "user-1", "Иванов");

        var online = presence.GetOnline();
        Assert.Single(online);
        Assert.Equal("Иванов", online[0].UserName);
        Assert.Equal(1, presence.OnlineCount);
    }

    [Fact]
    public void Две_сессии_одного_пользователя_считаются_одним_онлайном()
    {
        var (presence, _) = Create();

        presence.Register("blazor-1", SessionKind.Blazor, "user-1", "Иванов");
        presence.Register("hub-1", SessionKind.Hub, "user-1", "Иванов");

        var online = presence.GetOnline();
        var user = Assert.Single(online);
        Assert.Equal(2, user.SessionCount);
        Assert.Equal(1, presence.OnlineCount);
    }

    [Fact]
    public void Unregister_убирает_пользователя_когда_сессий_не_осталось()
    {
        var (presence, _) = Create();

        presence.Register("s1", SessionKind.Blazor, "user-1", "Иванов");
        presence.Register("s2", SessionKind.Blazor, "user-2", "Петров");

        presence.Unregister("s1");

        var online = presence.GetOnline();
        Assert.Single(online);
        Assert.Equal("Петров", online[0].UserName);
    }

    [Fact]
    public void Touch_обновляет_время_последней_активности()
    {
        var (presence, _) = Create();

        presence.Register("s1", SessionKind.Blazor, "user-1", "Иванов");
        var before = presence.GetOnline()[0].LastSeenUtc;

        Thread.Sleep(15);
        presence.Touch("s1");

        var after = presence.GetOnline()[0].LastSeenUtc;
        Assert.True(after > before, "LastSeenUtc должен увеличиться после Touch");
    }

    [Fact]
    public void Изменения_публикуются_в_шину()
    {
        var (presence, bus) = Create();

        var notifications = new List<IReadOnlyList<OnlineUser>>();
        bus.PresenceChanged += users => notifications.Add(users);

        presence.Register("s1", SessionKind.Blazor, "user-1", "Иванов");
        presence.Unregister("s1");

        Assert.Equal(2, notifications.Count);
        Assert.Single(notifications[0]);
        Assert.Empty(notifications[1]);
    }

    [Fact]
    public void Пустые_идентификаторы_игнорируются()
    {
        var (presence, _) = Create();

        presence.Register("", SessionKind.Blazor, "user-1", "Иванов");
        presence.Register("s1", SessionKind.Blazor, "", "Иванов");

        Assert.Equal(0, presence.OnlineCount);
    }
}
