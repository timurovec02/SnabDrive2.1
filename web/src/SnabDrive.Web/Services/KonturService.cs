using System.Globalization;
using System.Linq;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

public interface IKonturService
{
    /// <summary>Разбирает выгрузку Контур.Закупки (Excel) и складывает строки в «Избранное Контур».</summary>
    Task<Result<int>> ImportFromExcelAsync(Stream excelStream, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KonturFavorite>> GetFavoritesAsync(bool onlyNew, CancellationToken cancellationToken = default);

    /// <summary>Переносит выбранные строки в реестр (создаёт записи Regedit).</summary>
    Task<Result<int>> ImportToRegistryAsync(IEnumerable<int> favoriteIds, ChangeActor actor, CancellationToken cancellationToken = default);

    Task<Result> SetStatusAsync(IEnumerable<int> favoriteIds, KonturFavoriteStatus status, CancellationToken cancellationToken = default);
}

public sealed class KonturService : IKonturService
{
    private readonly IDbContextFactory<RegistryDbContext> _factory;
    private readonly IRegistryService _registry;

    public KonturService(IDbContextFactory<RegistryDbContext> factory, IRegistryService registry)
    {
        _factory = factory;
        _registry = registry;
    }

    public async Task<Result<int>> ImportFromExcelAsync(Stream excelStream, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        List<KonturFavorite> parsed;
        try
        {
            // Поток из InputFile (Blazor Server) не поддерживает синхронное чтение,
            // а ClosedXML читает синхронно — буферизуем в MemoryStream.
            using var buffer = new MemoryStream();
            await excelStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            parsed = ParseWorkbook(buffer, actor.UserName);
        }
        catch (Exception ex)
        {
            return Result<int>.Fail($"Не удалось разобрать файл: {ex.Message}");
        }

        if (parsed.Count == 0)
        {
            return Result<int>.Fail("В файле не найдено строк с данными.");
        }

        // Контроль дублей по номеру закупки (если он есть) среди ещё не отклонённых.
        var existingNumbers = await context.KonturFavorites
            .Where(f => f.Status != KonturFavoriteStatus.Rejected && f.PurchaseNumber != "")
            .Select(f => f.PurchaseNumber)
            .ToListAsync(cancellationToken);
        var existingSet = new HashSet<string>(existingNumbers, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var item in parsed)
        {
            if (!string.IsNullOrWhiteSpace(item.PurchaseNumber) && existingSet.Contains(item.PurchaseNumber))
            {
                continue;
            }

            context.KonturFavorites.Add(item);
            if (!string.IsNullOrWhiteSpace(item.PurchaseNumber))
            {
                existingSet.Add(item.PurchaseNumber);
            }

            added++;
        }

        if (added == 0)
        {
            return Result<int>.Fail("Все строки из файла уже есть в избранном.");
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException?.Message ?? ex.Message;
            return Result<int>.Fail($"Не удалось сохранить избранное. Убедитесь, что выполнен web/sql/kontur.sql. Причина: {inner}");
        }

        return Result<int>.Ok(added);
    }

    public async Task<IReadOnlyList<KonturFavorite>> GetFavoritesAsync(bool onlyNew, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var query = context.KonturFavorites.AsNoTracking();
        if (onlyNew)
        {
            query = query.Where(f => f.Status == KonturFavoriteStatus.New);
        }

        return await query.OrderByDescending(f => f.AddedAt).ThenByDescending(f => f.Id).ToListAsync(cancellationToken);
    }

    public async Task<Result<int>> ImportToRegistryAsync(IEnumerable<int> favoriteIds, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var ids = favoriteIds.Distinct().ToList();
        var favorites = await context.KonturFavorites
            .Where(f => ids.Contains(f.Id) && f.Status == KonturFavoriteStatus.New)
            .ToListAsync(cancellationToken);

        if (favorites.Count == 0)
        {
            return Result<int>.Fail("Не выбрано ни одной новой строки для переноса.");
        }

        var imported = 0;
        foreach (var favorite in favorites)
        {
            var request = new RegeditUpsertRequest
            {
                NameLink = string.IsNullOrWhiteSpace(favorite.NameLink) ? favorite.PurchaseNumber : favorite.NameLink,
                Customer = favorite.Customer,
                NMCK = favorite.NMCK,
                BiddingDate = favorite.BiddingDate,
                DateOfPlacement = favorite.DateOfPlacement,
                PlaceOfDelivery = favorite.PlaceOfDelivery,
                Winner = favorite.Winner,
                ResultPrice = favorite.ResultPrice
            };

            var created = await _registry.CreateAsync(request, actor, cancellationToken);
            if (created.Success && created.Value is not null)
            {
                favorite.Status = KonturFavoriteStatus.Imported;
                favorite.RegeditId = created.Value.Id;
                imported++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return imported > 0
            ? Result<int>.Ok(imported)
            : Result<int>.Fail("Не удалось перенести строки (проверьте права на колонки «Ссылка» и «Заказчик»).");
    }

    public async Task<Result> SetStatusAsync(IEnumerable<int> favoriteIds, KonturFavoriteStatus status, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var ids = favoriteIds.Distinct().ToList();
        var favorites = await context.KonturFavorites.Where(f => ids.Contains(f.Id)).ToListAsync(cancellationToken);
        foreach (var favorite in favorites)
        {
            favorite.Status = status;
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    // ------------------------------------------------------------------ разбор Excel

    private static List<KonturFavorite> ParseWorkbook(Stream stream, string addedBy)
    {
        var result = new List<KonturFavorite>();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        var rows = sheet.RowsUsed().ToList();
        if (rows.Count < 2)
        {
            return result;
        }

        var headerRow = rows[0];
        var cols = new List<(int Col, string Name)>();
        foreach (var cell in headerRow.CellsUsed())
        {
            cols.Add((cell.Address.ColumnNumber, cell.GetString().Trim()));
        }

        int? Find(params string[] names)
        {
            foreach (var c in cols)
            {
                if (names.Any(n => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)))
                {
                    return c.Col;
                }
            }

            return null;
        }

        int? FindContains(params string[] parts)
        {
            foreach (var c in cols)
            {
                if (parts.Any(pt => c.Name.Contains(pt, StringComparison.OrdinalIgnoreCase)))
                {
                    return c.Col;
                }
            }

            return null;
        }

        // Точная карта колонок выгрузки Контур. Дубликат «Название»:
        // первое — наименование закупки, второе (после «Регион») — заказчик.
        var colNumber = Find("Номер");
        var colName = Find("Название");
        var colNmck = Find("НМЦ");
        var colPlacement = Find("Дата публикации");
        var colBidding = Find("Проведение отбора") ?? Find("Окончание приема заявок");
        var colDelivery = Find("Место поставки");
        var colWinner = Find("Название победителя") ?? Find("Название поставщика");
        var colResult = Find("Предложение победителя") ?? Find("Цена договора");

        int? colCustomer = null;
        var regionCol = Find("Регион");
        if (regionCol is not null)
        {
            foreach (var c in cols)
            {
                if (c.Col > regionCol && c.Name == "Название")
                {
                    colCustomer = c.Col;
                    break;
                }
            }
        }

        colCustomer ??= FindContains("Размещает") ?? FindContains("заказчик", "организатор");
        colName ??= FindContains("наименован", "предмет", "объект");
        colNmck ??= FindContains("нмц", "цена", "бюджет");

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var raw = new Dictionary<string, string>();
            foreach (var (col, name) in cols)
            {
                raw[name] = sheet.Cell(row.RowNumber(), col).GetString().Trim();
            }

            var nameLink = Text(sheet, row.RowNumber(), colName);
            var purchaseNumber = Text(sheet, row.RowNumber(), colNumber);

            if (string.IsNullOrWhiteSpace(nameLink) && string.IsNullOrWhiteSpace(purchaseNumber))
            {
                continue;
            }

            result.Add(new KonturFavorite
            {
                PurchaseNumber = purchaseNumber,
                NameLink = string.IsNullOrWhiteSpace(nameLink) ? purchaseNumber : nameLink,
                Customer = Text(sheet, row.RowNumber(), colCustomer),
                NMCK = Money(sheet, row.RowNumber(), colNmck),
                BiddingDate = Date(sheet, row.RowNumber(), colBidding),
                DateOfPlacement = Date(sheet, row.RowNumber(), colPlacement),
                PlaceOfDelivery = Text(sheet, row.RowNumber(), colDelivery),
                Winner = Text(sheet, row.RowNumber(), colWinner),
                ResultPrice = Money(sheet, row.RowNumber(), colResult),
                Status = KonturFavoriteStatus.New,
                RawJson = JsonSerializer.Serialize(raw),
                AddedAt = DateTime.Now,
                AddedBy = addedBy
            });
        }

        return result;
    }

    private static string Text(IXLWorksheet sheet, int row, int? col) =>
        col is null ? string.Empty : sheet.Cell(row, col.Value).GetString().Trim();

    private static decimal Money(IXLWorksheet sheet, int row, int? col)
    {
        if (col is null)
        {
            return 0m;
        }

        var cell = sheet.Cell(row, col.Value);
        if (cell.DataType == XLDataType.Number)
        {
            return (decimal)cell.GetDouble();
        }

        var text = cell.GetString().Trim();
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    }

    private static DateTime? Date(IXLWorksheet sheet, int row, int? col)
    {
        if (col is null)
        {
            return null;
        }

        var cell = sheet.Cell(row, col.Value);
        if (cell.DataType == XLDataType.DateTime)
        {
            return cell.GetDateTime();
        }

        var text = cell.GetString().Trim();
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt;
        }

        return null;
    }
}
