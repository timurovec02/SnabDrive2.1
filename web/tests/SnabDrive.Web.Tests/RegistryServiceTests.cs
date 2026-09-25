using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;
using Xunit;

namespace SnabDrive.Web.Tests;

public class RegistryServiceTests
{
    // Каждый тест поднимает свою базу, чтобы тесты не мешали друг другу.
    private static async Task<(TestServices Svc, int PurchaseId)> NewAsync()
    {
        var svc = new TestServices();
        var (purchase, _, _) = await svc.SeedDictionariesAsync();
        return (svc, purchase);
    }

    [Fact]
    public async Task QueryAsync_возвращает_записи_отсортированные_по_Id()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            await svc.AddRegeditAsync(r => r.NameLink = "А");
            await svc.AddRegeditAsync(r => r.NameLink = "Б");

            var page = await svc.Registry.QueryAsync(new RegistryQuery());

            Assert.Equal(2, page.TotalCount);
            Assert.Equal(2, page.Items.Count);
            Assert.Equal("А", page.Items[0].NameLink);
            Assert.Equal("Б", page.Items[1].NameLink);
        }
    }

    [Fact]
    public async Task QueryAsync_ищет_по_заказчику_и_по_наименованию_справочника()
    {
        var (svc, purchaseId) = await NewAsync();
        using (svc)
        {
            await svc.AddRegeditAsync(r =>
            {
                r.Customer = "Газпром";
                r.TypeOfPurchaseId = purchaseId;
            });
            await svc.AddRegeditAsync(r => r.Customer = "Росатом");

            var byCustomer = await svc.Registry.QueryAsync(new RegistryQuery { Search = "газ" });
            Assert.Single(byCustomer.Items);
            Assert.Equal("Газпром", byCustomer.Items[0].Customer);

            var byDictionary = await svc.Registry.QueryAsync(new RegistryQuery { Search = "Аукцион" });
            Assert.Single(byDictionary.Items);
            Assert.Equal("Аукцион", byDictionary.Items[0].TypeOfPurchaseName);
        }
    }

    [Fact]
    public async Task QueryAsync_ищет_по_сумме_и_по_дате()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            await svc.AddRegeditAsync(r => { r.NMCK = 1234567m; r.BiddingDate = new DateTime(2025, 3, 10); });
            await svc.AddRegeditAsync(r => { r.NMCK = 50m; r.BiddingDate = new DateTime(2025, 7, 1); });

            var byAmount = await svc.Registry.QueryAsync(new RegistryQuery { Search = "1234567" });
            Assert.Single(byAmount.Items);

            var byDate = await svc.Registry.QueryAsync(new RegistryQuery { Search = "10.03.2025" });
            Assert.Single(byDate.Items);

            var byYear = await svc.Registry.QueryAsync(new RegistryQuery { Search = "2025" });
            Assert.Equal(2, byYear.Items.Count);
        }
    }

    [Fact]
    public async Task QueryAsync_сортирует_по_убыванию_НМЦК()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            await svc.AddRegeditAsync(r => r.NMCK = 10m);
            await svc.AddRegeditAsync(r => r.NMCK = 300m);
            await svc.AddRegeditAsync(r => r.NMCK = 20m);

            var page = await svc.Registry.QueryAsync(new RegistryQuery { SortBy = "NMCK", SortDescending = true });

            Assert.Equal(new decimal[] { 300m, 20m, 10m }, page.Items.Select(x => x.NMCK).ToArray());
        }
    }

    [Fact]
    public async Task QueryAsync_фильтрует_по_колонке_и_пагинирует()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            for (var i = 1; i <= 12; i++)
            {
                await svc.AddRegeditAsync(r => { r.Customer = i <= 7 ? "Основной" : "Прочий"; r.NMCK = i * 1000m; });
            }

            var filtered = await svc.Registry.QueryAsync(new RegistryQuery
            {
                ColumnFilters = new Dictionary<string, string> { ["Customer"] = "Основной" },
                PageSize = 5
            });

            Assert.Equal(7, filtered.TotalCount);
            Assert.Equal(5, filtered.Items.Count);
            Assert.Equal(2, filtered.TotalPages);
            Assert.True(filtered.HasNext);
            Assert.False(filtered.HasPrevious);

            var second = await svc.Registry.QueryAsync(new RegistryQuery
            {
                ColumnFilters = new Dictionary<string, string> { ["Customer"] = "Основной" },
                PageSize = 5,
                Page = 2
            });

            Assert.Equal(2, second.Items.Count);
            Assert.Equal(6, second.FirstItemIndex);
            Assert.Equal(7, second.LastItemIndex);
            Assert.False(second.HasNext);
        }
    }

    [Fact]
    public async Task QueryAsync_фильтр_по_сумме_поддерживает_операторы()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            await svc.AddRegeditAsync(r => r.NMCK = 500_000m);
            await svc.AddRegeditAsync(r => r.NMCK = 1_500_000m);
            await svc.AddRegeditAsync(r => r.NMCK = 3_000_000m);

            var big = await svc.Registry.QueryAsync(new RegistryQuery
            {
                ColumnFilters = new Dictionary<string, string> { ["NMCK"] = ">=1000000" }
            });

            Assert.Equal(2, big.TotalCount);

            var small = await svc.Registry.QueryAsync(new RegistryQuery
            {
                ColumnFilters = new Dictionary<string, string> { ["NMCK"] = "<1000000" }
            });

            Assert.Single(small.Items);
        }
    }

    [Fact]
    public async Task CreateAsync_отклоняет_пустое_наименование()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            var request = TestServices.ValidRequest() with { NameLink = "   " };

            var result = await svc.Registry.CreateAsync(request, TestServices.Actor);

            Assert.False(result.Success);
            Assert.True(result.FieldErrors.ContainsKey(nameof(RegeditUpsertRequest.NameLink)));
            Assert.Empty(svc.Notifier.Events);
        }
    }

    [Fact]
    public async Task CreateAsync_создаёт_запись_пишет_аудит_и_шлёт_событие()
    {
        var (svc, purchaseId) = await NewAsync();
        using (svc)
        {
            var request = TestServices.ValidRequest() with { TypeOfPurchaseId = purchaseId, Customer = "Новый заказчик" };

            var result = await svc.Registry.CreateAsync(request, TestServices.Actor);

            Assert.True(result.Success);
            Assert.NotNull(result.Value);
            Assert.True(result.Value!.Id > 0);
            Assert.Equal("Новый заказчик", result.Value.Customer);
            Assert.Equal("Аукцион", result.Value.TypeOfPurchaseName);

            Assert.Equal(1, await svc.Registry.CountAsync());

            var audit = await svc.Audit.QueryAsync(new AuditQuery());
            Assert.Single(audit.Items);
            Assert.Equal(AuditAction.Created, audit.Items[0].Action);
            Assert.Equal("Regedit", audit.Items[0].EntityName);

            var created = Assert.Single(svc.Notifier.Events);
            Assert.Equal(ChangeKind.Created, created.Kind);
        }
    }

    [Fact]
    public async Task UpdateAsync_меняет_поле_и_фиксирует_старое_и_новое_значение()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            var entity = await svc.AddRegeditAsync(r => r.Customer = "Старый");
            var request = TestServices.ValidRequest() with { Customer = "Новый", NMCK = 999m };

            var result = await svc.Registry.UpdateAsync(entity.Id, request, TestServices.Actor);

            Assert.True(result.Success);
            Assert.Equal("Новый", result.Value!.Customer);

            var audit = await svc.Audit.QueryAsync(new AuditQuery());
            var entry = Assert.Single(audit.Items);
            Assert.Equal(AuditAction.Updated, entry.Action);
            Assert.True(entry.Changes.ContainsKey("Заказчик"));
            Assert.Equal("Старый", entry.Changes["Заказчик"].Old);
            Assert.Equal("Новый", entry.Changes["Заказчик"].New);
        }
    }

    [Fact]
    public async Task UpdateAsync_без_изменений_не_пишет_аудит()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            var entity = await svc.AddRegeditAsync(r => { r.Customer = "Заказчик"; r.NMCK = 100m; });
            var request = TestServices.ValidRequest();

            var result = await svc.Registry.UpdateAsync(entity.Id, request, TestServices.Actor);

            Assert.True(result.Success);

            var audit = await svc.Audit.QueryAsync(new AuditQuery());
            Assert.Empty(audit.Items);
            Assert.Empty(svc.Notifier.Events);
        }
    }

    [Fact]
    public async Task DeleteAsync_удаляет_запись()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            var entity = await svc.AddRegeditAsync();

            var result = await svc.Registry.DeleteAsync(entity.Id, TestServices.Actor);

            Assert.True(result.Success);
            Assert.Equal(0, await svc.Registry.CountAsync());
            Assert.Null(await svc.Registry.GetByIdAsync(entity.Id));
        }
    }

    [Fact]
    public async Task ArchiveAsync_переносит_запись_в_архив_сохраняя_IdOld()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            var entity = await svc.AddRegeditAsync(r => r.NameLink = "Архивируемая");

            var result = await svc.Registry.ArchiveAsync(entity.Id, TestServices.Actor);

            Assert.True(result.Success);
            Assert.Equal(0, await svc.Registry.CountAsync());
            Assert.Equal(1, await svc.Registry.CountArchiveAsync());

            var archive = await svc.Registry.QueryArchiveAsync(new RegistryQuery());
            var row = Assert.Single(archive.Items);
            Assert.Equal(entity.Id, row.IdOld);
            Assert.Equal("Архивируемая", row.NameLink);
            Assert.NotNull(row.ArchivateDate);
            Assert.Equal(entity.Id, row.ColorOwnerId);
        }
    }

    [Fact]
    public async Task RestoreAsync_возвращает_запись_в_реестр()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            var entity = await svc.AddRegeditAsync(r => r.NameLink = "Возвращаемая");
            await svc.Registry.ArchiveAsync(entity.Id, TestServices.Actor);

            var archive = await svc.Registry.QueryArchiveAsync(new RegistryQuery());
            var archiveId = archive.Items[0].Id;

            var result = await svc.Registry.RestoreAsync(archiveId, TestServices.Actor);

            Assert.True(result.Success);
            Assert.Equal(1, await svc.Registry.CountAsync());
            Assert.Equal(0, await svc.Registry.CountArchiveAsync());

            var restored = await svc.Registry.QueryAsync(new RegistryQuery());
            Assert.Equal("Возвращаемая", restored.Items[0].NameLink);
        }
    }

    [Fact]
    public async Task SetCellColorAsync_создаёт_обновляет_и_снимает_подсветку()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            var entity = await svc.AddRegeditAsync();

            await svc.Registry.SetCellColorAsync(entity.Id, "Customer", "#ffeb3b", TestServices.Actor);
            var colors = await svc.Registry.GetCellColorsAsync(new[] { entity.Id });
            Assert.Equal("#ffeb3b", colors[$"{entity.Id}|Customer"]);

            await svc.Registry.SetCellColorAsync(entity.Id, "Customer", "#ffcdd2", TestServices.Actor);
            colors = await svc.Registry.GetCellColorsAsync(new[] { entity.Id });
            Assert.Single(colors);
            Assert.Equal("#ffcdd2", colors[$"{entity.Id}|Customer"]);

            await svc.Registry.SetCellColorAsync(entity.Id, "Customer", null, TestServices.Actor);
            colors = await svc.Registry.GetCellColorsAsync(new[] { entity.Id });
            Assert.Empty(colors);
        }
    }

    [Fact]
    public async Task SetCellColorAsync_отклоняет_неизвестную_колонку()
    {
        var (svc, _) = await NewAsync();
        using (svc)
        {
            var result = await svc.Registry.SetCellColorAsync(1, "NoSuchColumn", "#ffffff", TestServices.Actor);

            Assert.False(result.Success);
        }
    }
}
