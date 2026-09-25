using SnabDrive.Web.Domain;
using SnabDrive.Web.Services.Validation;
using Xunit;

namespace SnabDrive.Web.Tests;

public class ColumnAccessServiceTests
{
    [Fact]
    public async Task AllColumns_GivesUnrestrictedAccess()
    {
        using var services = new TestServices();
        var userId = await services.AddUserAsync("ivanov", ColumnAccessMode.AllColumns);

        var access = await services.ColumnAccess.GetAsync(userId);

        Assert.True(access.Unrestricted);
        Assert.True(access.CanView("NMCK"));
        Assert.True(access.CanEdit("NMCK"));
    }

    [Fact]
    public async Task CustomColumns_LimitsViewAndEdit()
    {
        using var services = new TestServices();
        var userId = await services.AddUserAsync("petrov", ColumnAccessMode.CustomColumns);

        await services.SetColumnPermissionsAsync(userId,
            new ColumnPermission("NameLink", CanView: true, CanEdit: true),
            new ColumnPermission("Customer", CanView: true, CanEdit: false),
            new ColumnPermission("NMCK", CanView: false, CanEdit: false));

        var access = await services.ColumnAccess.GetAsync(userId);

        Assert.False(access.Unrestricted);
        Assert.True(access.CanView("NameLink"));
        Assert.True(access.CanEdit("NameLink"));

        Assert.True(access.CanView("Customer"));
        Assert.False(access.CanEdit("Customer"));

        Assert.False(access.CanView("NMCK"));
        Assert.False(access.CanEdit("NMCK"));
    }

    [Fact]
    public async Task CustomColumnsWithoutRows_HidesEverything()
    {
        using var services = new TestServices();
        var userId = await services.AddUserAsync("sidorov", ColumnAccessMode.CustomColumns);

        var access = await services.ColumnAccess.GetAsync(userId);

        Assert.False(access.Unrestricted);
        Assert.Empty(access.Viewable);
        Assert.Empty(access.Editable);
        Assert.False(access.CanView("Customer"));
    }

    [Fact]
    public async Task Admin_IsNeverRestricted()
    {
        using var services = new TestServices();
        var userId = await services.AddUserAsync("boss", ColumnAccessMode.CustomColumns, isAdmin: true);

        var access = await services.ColumnAccess.GetAsync(userId);

        Assert.True(access.Unrestricted);
    }

    [Fact]
    public async Task UnknownUser_IsUnrestricted()
    {
        using var services = new TestServices();

        var access = await services.ColumnAccess.GetAsync("нет-такого");

        Assert.True(access.Unrestricted);
    }

    [Fact]
    public async Task SetAsync_ReplacesPreviousPermissions()
    {
        using var services = new TestServices();
        var userId = await services.AddUserAsync("petrov", ColumnAccessMode.CustomColumns);

        await services.SetColumnPermissionsAsync(userId, new ColumnPermission("NMCK", true, true));

        var setResult = await services.ColumnAccess.SetAsync(userId, ColumnAccessMode.CustomColumns,
            new[] { new ColumnPermission("Customer", true, false) }, TestServices.Actor);

        Assert.True(setResult.Success);

        var permissions = await services.ColumnAccess.GetPermissionsAsync(userId);
        Assert.Single(permissions);
        Assert.Equal("Customer", permissions[0].Key);
        Assert.False(permissions[0].CanEdit);
    }

    [Fact]
    public async Task SetAsync_RejectsUnknownColumn()
    {
        using var services = new TestServices();
        var userId = await services.AddUserAsync("petrov", ColumnAccessMode.CustomColumns);

        var result = await services.ColumnAccess.SetAsync(userId, ColumnAccessMode.CustomColumns,
            new[] { new ColumnPermission("ТакойКолонкиНет", true, true) }, TestServices.Actor);

        Assert.False(result.Success);
        Assert.Contains("ТакойКолонкиНет", result.Error);
    }

    [Fact]
    public async Task SetAsync_SwitchesToAllColumnsAndNotifies()
    {
        using var services = new TestServices();
        var userId = await services.AddUserAsync("petrov", ColumnAccessMode.CustomColumns);
        await services.SetColumnPermissionsAsync(userId, new ColumnPermission("NMCK", true, true));

        var result = await services.ColumnAccess.SetAsync(userId, ColumnAccessMode.AllColumns,
            Array.Empty<ColumnPermission>(), TestServices.Actor);

        Assert.True(result.Success);
        Assert.Equal(ColumnAccessMode.AllColumns, await services.ColumnAccess.GetModeAsync(userId));
        Assert.Empty(await services.ColumnAccess.GetPermissionsAsync(userId));
        Assert.True((await services.ColumnAccess.GetAsync(userId)).Unrestricted);

        Assert.Contains(services.Notifier.Events, e => e.EntityName == "UserColumnPermission");
    }

    [Fact]
    public void Build_EditWithoutView_IsNormalizedToNoAccess()
    {
        var map = ColumnAccessMap.Build(new[] { new ColumnPermission("NMCK", CanView: false, CanEdit: true) });

        Assert.False(map.CanView("NMCK"));
        Assert.False(map.CanEdit("NMCK"));
    }

    [Fact]
    public void Full_IsUnrestrictedForEveryColumn()
    {
        foreach (var column in RegistryColumns.All)
        {
            Assert.True(ColumnAccessMap.Full.CanView(column.Key));
            Assert.True(ColumnAccessMap.Full.CanEdit(column.Key));
        }
    }

    [Fact]
    public void EveryRegistryColumnKeyIsAcceptedBySetAsync_Validation()
    {
        // Страховка от опечаток: все ключи из RegistryColumns должны быть известны сервису прав.
        Assert.All(RegistryColumns.All, column => Assert.True(RegistryColumns.IsKnown(column.Key)));
    }
}

public class ColumnAccessEnforcementTests
{
    [Fact]
    public async Task QueryAsync_MasksColumnsWithoutViewRight()
    {
        using var services = new TestServices();
        await services.AddRegeditAsync(e =>
        {
            e.NameLink = "Секретная закупка";
            e.Customer = "Секретный заказчик";
            e.NMCK = 5_000_000m;
            e.Winner = "Секретный победитель";
        });

        var access = ColumnAccessMap.Build(new[]
        {
            new ColumnPermission("NameLink", true, true),
            new ColumnPermission("Customer", false, false),
            new ColumnPermission("NMCK", false, false),
            new ColumnPermission("Winner", false, false)
        });

        var page = await services.Registry.QueryAsync(new RegistryQuery(), access);

        Assert.Single(page.Items);
        var row = page.Items[0];

        Assert.Equal("Секретная закупка", row.NameLink);
        Assert.Equal(string.Empty, row.Customer);
        Assert.Equal(0m, row.NMCK);
        Assert.Equal(string.Empty, row.Winner);
    }

    [Fact]
    public async Task GetByIdAsync_MasksHiddenColumns()
    {
        using var services = new TestServices();
        var entity = await services.AddRegeditAsync(e => e.NMCK = 1_234m);

        var access = ColumnAccessMap.Build(new[] { new ColumnPermission("NameLink", true, true) });

        var row = await services.Registry.GetByIdAsync(entity.Id, access);

        Assert.NotNull(row);
        Assert.Equal(0m, row!.NMCK);
        Assert.Equal(string.Empty, row.Customer);
    }

    [Fact]
    public async Task GetEditModelAsync_MasksHiddenFields()
    {
        using var services = new TestServices();
        var entity = await services.AddRegeditAsync(e =>
        {
            e.Customer = "Скрытый заказчик";
            e.NMCK = 777m;
        });

        var access = ColumnAccessMap.Build(new[] { new ColumnPermission("NameLink", true, true) });

        var model = await services.Registry.GetEditModelAsync(entity.Id, access);

        Assert.NotNull(model);
        Assert.Equal(string.Empty, model!.Customer);
        Assert.Equal(0m, model.NMCK);
    }

    [Fact]
    public async Task UpdateAsync_IgnoresFieldsWithoutEditRight()
    {
        using var services = new TestServices();
        var entity = await services.AddRegeditAsync(e =>
        {
            e.Customer = "Исходный заказчик";
            e.NMCK = 100m;
        });

        var access = ColumnAccessMap.Build(new[]
        {
            new ColumnPermission("NameLink", true, true),
            new ColumnPermission("Customer", true, false),
            new ColumnPermission("NMCK", true, true)
        });

        var request = TestServices.ValidRequest() with
        {
            Customer = "Заказчик, которого менять нельзя",
            NMCK = 999m
        };

        var actor = new ChangeActor("user-2", "petrov") { Access = access };
        var result = await services.Registry.UpdateAsync(entity.Id, request, actor);

        Assert.True(result.Success);
        Assert.Equal(999m, result.Value!.NMCK);
        Assert.Equal("Исходный заказчик", result.Value.Customer);
    }

    [Fact]
    public async Task UpdateAsync_WithoutRight_DoesNotWriteAuditForThatField()
    {
        using var services = new TestServices();
        var entity = await services.AddRegeditAsync(e => e.Customer = "Исходный заказчик");

        var access = ColumnAccessMap.Build(new[] { new ColumnPermission("NameLink", true, true) });
        var actor = new ChangeActor("user-2", "petrov") { Access = access };

        var result = await services.Registry.UpdateAsync(entity.Id,
            TestServices.ValidRequest() with { Customer = "Чужой заказчик" }, actor);

        Assert.True(result.Success);

        var audit = await services.Audit.QueryAsync(new AuditQuery { EntityName = "Regedit" });

        Assert.DoesNotContain(
            audit.Items.Where(a => a.EntityId == entity.Id).SelectMany(a => a.Changes.Values),
            c => c.New == "Чужой заказчик");
    }

    [Fact]
    public async Task CreateAsync_BlockedWhenRequiredFieldsAreNotEditable()
    {
        using var services = new TestServices();

        var access = ColumnAccessMap.Build(new[] { new ColumnPermission("NameLink", true, false) });
        var actor = new ChangeActor("user-2", "petrov") { Access = access };

        var result = await services.Registry.CreateAsync(TestServices.ValidRequest(), actor);

        Assert.False(result.Success);
        Assert.Contains("права", result.Error);
    }

    [Fact]
    public async Task CreateAsync_WorksWhenRequiredFieldsAreEditable()
    {
        using var services = new TestServices();

        var access = ColumnAccessMap.Build(new[]
        {
            new ColumnPermission("NameLink", true, true),
            new ColumnPermission("Customer", true, true),
            new ColumnPermission("NMCK", true, true)
        });
        var actor = new ChangeActor("user-2", "petrov") { Access = access };

        var result = await services.Registry.CreateAsync(TestServices.ValidRequest(), actor);

        Assert.True(result.Success);
        Assert.Equal(100m, result.Value!.NMCK);
    }

    [Fact]
    public void Validator_SkipsNonEditableRequiredFields()
    {
        var access = ColumnAccessMap.Build(new[] { new ColumnPermission("NMCK", true, true) });

        var errors = RegistryValidator.Validate(new RegeditUpsertRequest(), access);

        Assert.DoesNotContain("NameLink", errors.Keys);
        Assert.DoesNotContain("Customer", errors.Keys);
    }

    [Fact]
    public void Validator_StillChecksEditableFields()
    {
        var access = ColumnAccessMap.Build(new[]
        {
            new ColumnPermission("NameLink", true, true),
            new ColumnPermission("Customer", true, true),
            new ColumnPermission("NMCK", true, true)
        });

        var errors = RegistryValidator.Validate(new RegeditUpsertRequest(), access);

        Assert.Contains("NameLink", errors.Keys);
        Assert.Contains("Customer", errors.Keys);
    }

    [Fact]
    public void Validator_DefaultOverloadBehavesLikeFullAccess()
    {
        var request = TestServices.ValidRequest() with { Customer = "" };

        Assert.Contains("Customer", RegistryValidator.Validate(request).Keys);
        Assert.Contains("Customer", RegistryValidator.Validate(request, ColumnAccessMap.Full).Keys);
    }
}
