using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;
using Xunit;

namespace SnabDrive.Web.Tests;

public class AuditServiceTests
{
    [Fact]
    public async Task WriteAsync_сохраняет_событие_с_изменениями_полей()
    {
        using var svc = new TestServices();

        await svc.Audit.WriteAsync(
            AuditAction.Updated,
            "Regedit",
            42,
            TestServices.Actor,
            new Dictionary<string, FieldChange> { ["Заказчик"] = new("Старый", "Новый") },
            "Изменена запись");

        var page = await svc.Audit.QueryAsync(new AuditQuery());

        var entry = Assert.Single(page.Items);
        Assert.Equal(AuditAction.Updated, entry.Action);
        Assert.Equal("Regedit", entry.EntityName);
        Assert.Equal(42, entry.EntityId);
        Assert.Equal("ivanov", entry.UserName);
        Assert.Equal("Изменена запись", entry.Summary);
        Assert.Equal("Старый", entry.Changes["Заказчик"].Old);
        Assert.Equal("Новый", entry.Changes["Заказчик"].New);
    }

    [Fact]
    public async Task QueryAsync_фильтрует_по_сущности_операции_и_пользователю()
    {
        using var svc = new TestServices();

        await svc.Audit.WriteAsync(AuditAction.Created, "Regedit", 1, new ChangeActor("u1", "ivanov"));
        await svc.Audit.WriteAsync(AuditAction.Deleted, "Regedit", 2, new ChangeActor("u2", "petrov"));
        await svc.Audit.WriteAsync(AuditAction.Login, "User", 0, new ChangeActor("u1", "ivanov"));

        var regedit = await svc.Audit.QueryAsync(new AuditQuery { EntityName = "Regedit" });
        Assert.Equal(2, regedit.TotalCount);

        var deleted = await svc.Audit.QueryAsync(new AuditQuery { Action = AuditAction.Deleted });
        Assert.Single(deleted.Items);

        var petrov = await svc.Audit.QueryAsync(new AuditQuery { UserName = "petr" });
        Assert.Single(petrov.Items);

        var ivanovLogins = await svc.Audit.QueryAsync(new AuditQuery { EntityName = "User", UserName = "ivanov" });
        Assert.Single(ivanovLogins.Items);
    }

    [Fact]
    public async Task QueryAsync_сортирует_от_новых_к_старым()
    {
        using var svc = new TestServices();

        await svc.Audit.WriteAsync(AuditAction.Created, "Regedit", 1, TestServices.Actor, summary: "первое");
        await svc.Audit.WriteAsync(AuditAction.Updated, "Regedit", 1, TestServices.Actor, summary: "второе");

        var page = await svc.Audit.QueryAsync(new AuditQuery());

        Assert.Equal("второе", page.Items[0].Summary);
        Assert.Equal("первое", page.Items[1].Summary);
    }
}
