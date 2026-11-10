using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

public interface ILegacyImportService
{
    /// <summary>Разбирает старую выгрузку реестра (CSV/XLSX) и показывает, что куда ляжет.</summary>
    Task<Result<LegacyImportPreview>> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Переносит разобранные строки в dbo.RegistryItems, пропуская дубликаты.</summary>
    Task<Result<LegacyImportReport>> ImportAsync(IReadOnlyList<LegacyImportRow> rows, ChangeActor actor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Импорт старой выгрузки реестра («Реестр закупок_2025 … ОБЩИЙ.csv») в dbo.RegistryItems.
///
/// Правила те же, что в web/tools/import_legacy_registry.py (он строит SQL-скрипт
/// для SSMS, если залить данные надо без публикации сайта):
///   • берём только колонки A..Q — «Статус исполнения контракта» и колонки без
///     заголовка (рабочие пометки в несколько строк) не переносим;
///   • «Срок поставки» → «Сроки доставки» (DeliveryTerms), дата доставки не выдумывается;
///   • даты и суммы в файле записаны свободным текстом («подписан 05.11.2025»,
///     «380000швв звони») — берём первое число/дату, остальной текст не теряем:
///     он уходит в примечание;
///   • справочники не создаём: тип закупки и статус B2B связываются только с тем,
///     что уже есть в базе по названию;
///   • стадия выводится из данных: подписан контракт → 8, есть итог → 6,
///     есть торги → 4, размещена → 3, иначе 1.
/// </summary>
public sealed class LegacyImportService : ILegacyImportService
{
    /// <summary>Колонка A — ссылка на закупку или текст площадки («АСТ-ГОЗ», «Прямой договор»).</summary>
    private const int FirstColumn = 0;

    /// <summary>Колонка Q — последняя, которую берём («Примечание»).</summary>
    private const int LastColumn = 16;

    private const int NameLimit = 1000;
    private const int LinkLimit = 1000;
    private const int NumberLimit = 200;
    private const int TextLimit = 1000;
    private const int TermsLimit = 500;
    private const int NoteLimit = 4000;

    /// <summary>Поля, на которые раскладывается строка файла.</summary>
    private enum Field
    {
        SourceRef,
        Customer,
        TypeText,
        PlaceOfDelivery,
        ReserveNumber,
        NationalMode,
        B2BText,
        DateOfTransferForPlacement,
        DateOfPlacement,
        BiddingDate,
        DateResults,
        NMCK,
        MinPrice,
        ResultPrice,
        DateOfConclusionOfTheContract,
        DeliveryTerms,
        Note
    }

    /// <summary>Заголовок колонки (без пробелов и знаков, в нижнем регистре) → поле.</summary>
    private static readonly Dictionary<string, Field> ColumnMap = new(StringComparer.Ordinal)
    {
        ["заказчик"] = Field.Customer,
        ["типзакупкизакон"] = Field.TypeText,
        ["типзакупки"] = Field.TypeText,
        ["местопоставкитовара"] = Field.PlaceOfDelivery,
        ["местопоставки"] = Field.PlaceOfDelivery,
        ["номеррезерва"] = Field.ReserveNumber,
        ["нацрежимограничениепогодам"] = Field.NationalMode,
        ["нацрежим"] = Field.NationalMode,
        ["статусрезервав2в"] = Field.B2BText,
        ["статусрезерваb2b"] = Field.B2BText,
        ["датапередачинаразмещение"] = Field.DateOfTransferForPlacement,
        ["датаразмещениянаплощадке"] = Field.DateOfPlacement,
        ["дататоргов"] = Field.BiddingDate,
        ["датаподведенияитогов"] = Field.DateResults,
        ["нмцк"] = Field.NMCK,
        ["нашаминимальнаясумма"] = Field.MinPrice,
        ["итоговаясумма"] = Field.ResultPrice,
        ["датазаключенияконтракта"] = Field.DateOfConclusionOfTheContract,
        ["срокпоставки"] = Field.DeliveryTerms,
        ["примечание"] = Field.Note,
        ["примечания"] = Field.Note
    };

    /// <summary>Заголовки для отчёта — как их видит пользователь.</summary>
    private static readonly Dictionary<Field, string> FieldTitles = new()
    {
        [Field.Customer] = "Заказчик",
        [Field.TypeText] = "Тип закупки",
        [Field.PlaceOfDelivery] = "Место поставки товара",
        [Field.ReserveNumber] = "Номер резерва",
        [Field.NationalMode] = "Нац режим",
        [Field.B2BText] = "Статус резерва B2B",
        [Field.DateOfTransferForPlacement] = "Дата передачи на размещение",
        [Field.DateOfPlacement] = "Дата размещения на площадке",
        [Field.BiddingDate] = "Дата торгов",
        [Field.DateResults] = "Дата подведения итогов",
        [Field.NMCK] = "НМЦК",
        [Field.MinPrice] = "Наша минимальная сумма",
        [Field.ResultPrice] = "Итоговая сумма",
        [Field.DateOfConclusionOfTheContract] = "Дата заключения контракта",
        [Field.DeliveryTerms] = "Сроки поставки",
        [Field.Note] = "Примечание"
    };

    /// <summary>Сокращения, которые нельзя писать со строчной буквы.</summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.Ordinal)
    {
        "ООО", "АО", "ПАО", "ОАО", "ИП", "ФГУП", "ГУП", "МУП", "ГБУ", "ГБУЗ", "МБУ",
        "МБУЗ", "МБОУ", "ГБОУ", "ФГБУ", "ФГБОУ", "ГКУ", "МКУ", "МКУП", "ФКУ", "ФГКУ",
        "ЦРБ", "РЖД", "НЭСК", "ГРЧЦ", "ЕИС", "ГОЗ", "РФ", "СНГ", "УВО", "БГ", "НДС"
    };

    /// <summary>
    /// Верхняя половина таблицы Windows-1251: Excel в русской локали сохраняет CSV
    /// именно в ней, а в .NET эта кодировка доступна только через отдельный пакет.
    /// Байт 0x98 в code page не определён — по спецификации Windows там «˜».
    /// </summary>
    private const string Windows1251UpperHalf =
        "ЂЃ‚ѓ„…†‡€‰Љ‹ЊЌЋЏђ‘’“”•–—˜™љ›њќћџ" + "\u00A0" + "ЎўЈ¤Ґ¦§Ё©Є«¬­®Ї°±Ііґµ¶·ё№є»јЅѕї" +
        "АБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдежзийклмнопрстуфхцчшщъыьэюя";

    private static readonly Regex DateRegex = new(@"\b(\d{1,2})\.(\d{1,2})\.(\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex IsoDateRegex = new(@"\b(\d{4})-(\d{1,2})-(\d{1,2})\b", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"^[^\d]{0,6}(\d{1,2})[:.](\d{2})", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(
        @"\d{1,3}(?:[ \t\u00A0\u202F]\d{3})+(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://\S+|www\.\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex QuotedRegex = new("[«\"]([^«»\"]{6,})[»\"]", RegexOptions.Compiled);

    private readonly IDbContextFactory<RegistryDbContext> _factory;
    private readonly IAuditService _audit;
    private readonly IRegistryNotifier _notifier;
    private readonly ILogger<LegacyImportService> _logger;

    public LegacyImportService(
        IDbContextFactory<RegistryDbContext> factory,
        IAuditService audit,
        IRegistryNotifier notifier,
        ILogger<LegacyImportService> logger)
    {
        _factory = factory;
        _audit = audit;
        _notifier = notifier;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Разбор
    // -----------------------------------------------------------------------
    public async Task<Result<LegacyImportPreview>> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        List<List<string>> rows;
        try
        {
            // Поток из InputFile (Blazor Server) не читается синхронно — буферизуем.
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            rows = IsWorkbook(bytes, fileName)
                ? ReadWorkbook(buffer)
                : SplitCsv(Decode(bytes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось разобрать файл импорта {File}", fileName);
            return Result<LegacyImportPreview>.Fail($"Не удалось прочитать файл: {ex.Message}");
        }

        var headerIndex = FindHeaderRow(rows);
        if (headerIndex < 0)
        {
            return Result<LegacyImportPreview>.Fail(
                "Не найдена строка заголовков («Заказчик» и «НМЦК») в первых шести строках файла.");
        }

        var mapping = MapColumns(rows[headerIndex], out var ignored);
        if (mapping.Count == 0)
        {
            return Result<LegacyImportPreview>.Fail("В заголовках не найдено ни одного знакомого поля.");
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var types = await LoadTypesAsync(context, cancellationToken);
        var statuses = await LoadStatusesAsync(context, cancellationToken);

        var preview = new LegacyImportPreview { FileName = string.IsNullOrWhiteSpace(fileName) ? "выгрузка реестра" : fileName };
        var warnings = new Dictionary<string, int>(StringComparer.Ordinal);
        var unknownTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var unknownStatuses = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = headerIndex + 1; index < rows.Count; index++)
        {
            var cells = rows[index];
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var row = ParseRow(index + 1, cells, mapping, types, statuses, warnings, unknownTypes, unknownStatuses);
            preview.Rows.Add(row);
        }

        if (preview.Rows.Count == 0)
        {
            return Result<LegacyImportPreview>.Fail("В файле не найдено строк с данными.");
        }

        await MarkDuplicatesAsync(context, preview.Rows, cancellationToken);
        BuildStats(preview, ignored, warnings, unknownTypes, unknownStatuses);

        return Result<LegacyImportPreview>.Ok(preview);
    }

    private static LegacyImportRow ParseRow(
        int sourceRow,
        List<string> cells,
        IReadOnlyDictionary<int, Field> mapping,
        IReadOnlyDictionary<string, int> types,
        IReadOnlyDictionary<string, int> statuses,
        IDictionary<string, int> warnings,
        IDictionary<string, int> unknownTypes,
        IDictionary<string, int> unknownStatuses)
    {
        string Cell(Field field)
        {
            foreach (var pair in mapping)
            {
                if (pair.Value == field)
                {
                    return pair.Key < cells.Count ? cells[pair.Key].Trim() : string.Empty;
                }
            }

            return string.Empty;
        }

        void Warn(string message) => warnings[message] = warnings.TryGetValue(message, out var count) ? count + 1 : 1;

        var row = new LegacyImportRow
        {
            SourceRow = sourceRow,
            Customer = Clean(Cell(Field.Customer), 2000),
            PlaceOfDelivery = Clean(Cell(Field.PlaceOfDelivery), TextLimit),
            ReserveNumber = Clean(Cell(Field.ReserveNumber), TextLimit),
            NationalMode = Clean(Cell(Field.NationalMode), TextLimit),
            DeliveryTerms = Clean(Cell(Field.DeliveryTerms), TermsLimit)
        };

        // «По заявкам» в сроке поставки — та же отметка, что и на карточке этапа 1.
        row.IsByRequest = row.DeliveryTerms.Contains("по заявк", StringComparison.OrdinalIgnoreCase);

        // --- колонка A: ссылка на закупку или текст площадки -------------------
        var reference = Clean(Cell(Field.SourceRef), LinkLimit).Replace("\n", " ");
        var url = UrlRegex.Match(reference);
        var platform = string.Empty;
        if (url.Success)
        {
            row.EisLink = Clamp(url.Value.Trim(), LinkLimit);
            row.PurchaseNumber = ExtractNumber(url.Value) ?? string.Empty;
        }
        else
        {
            platform = reference;
        }

        // --- тип закупки и статус B2B: только то, что уже есть в справочниках --
        row.TypeText = Clean(Cell(Field.TypeText), NumberLimit);
        if (row.TypeText.Length > 0)
        {
            row.TypeOfPurchaseId = Lookup(types, row.TypeText, unknownTypes, "тип закупки не найден в справочнике", Warn);
        }
        else
        {
            Warn("тип закупки не указан");
        }

        row.B2BText = Clean(Cell(Field.B2BText), NumberLimit);
        if (row.B2BText.Length > 0)
        {
            row.B2BStatusId = Lookup(statuses, row.B2BText, unknownStatuses, "статус B2B не найден в справочнике", Warn);

            // «Согласован» в файле — это та же отметка, что и на карточке этапа 2.
            row.IsApproved = row.B2BText.Equals("согласован", StringComparison.OrdinalIgnoreCase);
        }

        // --- даты --------------------------------------------------------------
        var extras = new List<string>();
        foreach (var field in new[]
                 {
                     Field.DateOfTransferForPlacement, Field.DateOfPlacement, Field.BiddingDate,
                     Field.DateResults, Field.DateOfConclusionOfTheContract
                 })
        {
            var raw = Cell(field);
            var (moment, leftover) = ParseDate(raw);
            switch (field)
            {
                case Field.DateOfTransferForPlacement: row.DateOfTransferForPlacement = moment; break;
                case Field.DateOfPlacement: row.DateOfPlacement = moment; break;
                case Field.BiddingDate: row.BiddingDate = moment; break;
                case Field.DateResults: row.DateResults = moment; break;
                default: row.DateOfConclusionOfTheContract = moment; break;
            }

            if (moment is null && raw.Trim().Length > 0)
            {
                Warn("дата не распознана");
                row.Problems.Add($"{FieldTitles[field]}: {Clamp(raw.Trim(), 120)}");
            }

            if (leftover.Length > 0)
            {
                // Текст вокруг даты («торгов не будет, мы единственные») не выбрасываем.
                extras.Add($"{FieldTitles[field]}: {Clamp(leftover, 200)}");
            }
        }

        // --- суммы -------------------------------------------------------------
        foreach (var field in new[] { Field.NMCK, Field.MinPrice, Field.ResultPrice })
        {
            var raw = Cell(field);
            var (amount, call) = ParseMoney(raw);
            switch (field)
            {
                case Field.NMCK: row.NMCK = amount ?? 0m; break;
                case Field.MinPrice: row.MinPrice = amount ?? 0m; break;
                default: row.ResultPrice = amount ?? 0m; break;
            }

            if (amount is null && raw.Trim().Length > 0)
            {
                Warn("сумма не распознана");
                row.Problems.Add($"{FieldTitles[field]}: {Clamp(raw.Trim(), 120)}");
            }

            // «Звони» относится к минимальной сумме на торгах — там и отмечаем.
            if (call && field == Field.MinPrice)
            {
                row.IsCall = true;
            }
        }

        // --- контракт ----------------------------------------------------------
        var contract = Cell(Field.DateOfConclusionOfTheContract);
        if (contract.Contains("подписан", StringComparison.OrdinalIgnoreCase) ||
            contract.Contains("скан", StringComparison.OrdinalIgnoreCase))
        {
            row.ContractStatusId = 3;   // «Подписан»
        }

        // --- примечание + всё, чему не нашлось своего поля ----------------------
        var note = Clean(Cell(Field.Note), NoteLimit);
        var parts = new List<string>();
        if (note.Length > 0)
        {
            parts.Add(note);
        }

        parts.AddRange(extras);
        var combined = string.Join("\n", parts);
        row.Description = combined;
        row.Note = combined;

        // --- наименование: заказчик + тип закупки -------------------------------
        var kind = row.TypeText.Length > 0 ? row.TypeText : platform.Length > 0 ? platform : "Закупка";
        var baseName = ShortCustomer(row.Customer);
        row.NameLink = Clean($"{(baseName.Length > 0 ? baseName : "Закупка без заказчика")} — {kind}", NameLimit);

        if (row.Customer.Length == 0)
        {
            row.Problems.Add("нет заказчика");
        }

        if (row.PurchaseNumber.Length == 0)
        {
            Warn("нет номера закупки (дубль проверяется по заказчику и НМЦК)");
        }

        row.Stage = DeriveStage(row);
        return row;
    }

    /// <summary>Стадия по данным файла: чем дальше зашла закупка, тем позже этап.</summary>
    private static int DeriveStage(LegacyImportRow row)
    {
        if (row.DateOfConclusionOfTheContract is not null)
        {
            return 8;   // контракт подписан → «8. Ожидание доставки»
        }

        if (row.ResultPrice > 0)
        {
            return 6;   // есть итоговая сумма → «6. Контракт»
        }

        if (row.BiddingDate is not null)
        {
            return 4;   // торги назначены → «4. Торги»
        }

        if (row.DateOfPlacement is not null)
        {
            return 3;   // размещена на площадке → «3. Подача заявки»
        }

        // Отметка «Согласован» в сайте тоже ведёт закупку на этап подачи заявки.
        return row.IsApproved ? 3 : 1;
    }

    // -----------------------------------------------------------------------
    // Импорт
    // -----------------------------------------------------------------------
    public async Task<Result<LegacyImportReport>> ImportAsync(IReadOnlyList<LegacyImportRow> rows, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
        {
            return Result<LegacyImportReport>.Fail("Нет строк для импорта — сначала выберите файл.");
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var types = await LoadTypesAsync(context, cancellationToken);
        var statuses = await LoadStatusesAsync(context, cancellationToken);

        var numbers = new HashSet<string>(
            await context.Regedit.Where(r => r.PurchaseNumber != "").Select(r => r.PurchaseNumber).ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);
        var existing = await context.Regedit
            .Select(r => new { r.Customer, r.NMCK, r.DateOfPlacement })
            .ToListAsync(cancellationToken);
        var keys = new HashSet<string>(
            existing.Select(x => IdentityKey(x.Customer, x.NMCK, x.DateOfPlacement)),
            StringComparer.Ordinal);

        var imported = 0;
        var skipped = 0;
        var moment = DateTime.UtcNow;

        foreach (var row in rows)
        {
            var number = row.PurchaseNumber.Trim();
            var key = IdentityKey(row.Customer, row.NMCK, row.DateOfPlacement);
            if ((number.Length > 0 && numbers.Contains(number)) || keys.Contains(key))
            {
                skipped++;
                continue;
            }

            var typeName = row.TypeText.Trim();
            var statusName = row.B2BText.Trim();

            context.Regedit.Add(new Regedit
            {
                NameLink = Clamp(row.NameLink, NameLimit),
                EisLink = row.EisLink is { Length: > 0 } link ? Clamp(link, LinkLimit) : null,
                PurchaseNumber = Clamp(number, NumberLimit),
                Customer = row.Customer,
                PlaceOfDelivery = Clamp(row.PlaceOfDelivery, TextLimit),
                ReserveNumber = Clamp(row.ReserveNumber, TextLimit),
                NationalMode = Clamp(row.NationalMode, TextLimit),
                DeliveryTerms = row.DeliveryTerms.Length > 0 ? Clamp(row.DeliveryTerms, TermsLimit) : null,
                Description = row.Description,
                Note = row.Note,
                DateOfTransferForPlacement = row.DateOfTransferForPlacement,
                DateOfPlacement = row.DateOfPlacement,
                BiddingDate = row.BiddingDate,
                DateResults = row.DateResults,
                DateOfConclusionOfTheContract = row.DateOfConclusionOfTheContract,
                NMCK = row.NMCK,
                MinPrice = row.MinPrice,
                ResultPrice = row.ResultPrice,
                // Справочники не пополняем: связываем только с тем, что уже есть по названию.
                TypeOfPurchaseId = types.TryGetValue(typeName.ToLowerInvariant(), out var typeId) ? (int?)typeId : null,
                B2BStatusId = statuses.TryGetValue(statusName.ToLowerInvariant(), out var statusId) ? (int?)statusId : null,
                IsApproved = row.IsApproved,
                IsCall = row.IsCall,
                IsByRequest = row.IsByRequest,
                ContractStatusId = row.ContractStatusId,
                Stage = row.Stage,
                StageStartedAtUtc = moment,
                StageStartedBy = string.IsNullOrWhiteSpace(actor.UserName) ? null : Clamp(actor.UserName, 256)
            });

            if (number.Length > 0)
            {
                numbers.Add(number);
            }

            keys.Add(key);
            imported++;
        }

        if (imported == 0)
        {
            return Result<LegacyImportReport>.Ok(new LegacyImportReport(0, skipped));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить импортированные записи реестра");
            return Result<LegacyImportReport>.Fail(
                $"Не удалось сохранить импортированные записи. {ErrorText.Describe(ex)}");
        }

        var summary = $"Импорт реестра из файла: добавлено {imported} записей" +
                      (skipped > 0 ? $", пропущено дублей: {skipped}" : string.Empty);

        await _audit.WriteAsync(AuditAction.Created, RegistryService.EntityName, 0, actor, null, summary, cancellationToken);
        await _notifier.PublishAsync(new RegistryChangeEvent(
            ChangeKind.Created, RegistryService.EntityName, 0, actor.UserName, DateTime.UtcNow, summary), cancellationToken);

        return Result<LegacyImportReport>.Ok(new LegacyImportReport(imported, skipped));
    }

    // -----------------------------------------------------------------------
    // Помощники разбора
    // -----------------------------------------------------------------------
    private static async Task<Dictionary<string, int>> LoadTypesAsync(RegistryDbContext context, CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in await context.TypesOfPurchase.AsNoTracking().ToListAsync(cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(item.NameOfPurchase))
            {
                map.TryAdd(item.NameOfPurchase.Trim().ToLowerInvariant(), item.Id);
            }
        }

        return map;
    }

    private static async Task<Dictionary<string, int>> LoadStatusesAsync(RegistryDbContext context, CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in await context.B2BStatuses.AsNoTracking().ToListAsync(cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(item.NameB2B))
            {
                map.TryAdd(item.NameB2B.Trim().ToLowerInvariant(), item.Id);
            }
        }

        return map;
    }

    /// <summary>
    /// Значение из файла → идентификатор справочника. Справочники не пополняем:
    /// если такого названия в базе нет, поле остаётся пустым, а значение попадает в отчёт.
    /// </summary>
    private static int? Lookup(
        IReadOnlyDictionary<string, int> dictionary,
        string value,
        IDictionary<string, int> unknown,
        string warning,
        Action<string> warn)
    {
        if (dictionary.TryGetValue(value.Trim().ToLowerInvariant(), out var id))
        {
            return id;
        }

        unknown[value] = unknown.TryGetValue(value, out var count) ? count + 1 : 1;
        warn(warning);
        return null;
    }

    private static async Task MarkDuplicatesAsync(RegistryDbContext context, List<LegacyImportRow> rows, CancellationToken cancellationToken)
    {
        var numbers = new HashSet<string>(
            await context.Regedit.Where(r => r.PurchaseNumber != "").Select(r => r.PurchaseNumber).ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(
            (await context.Regedit.Select(r => new { r.Customer, r.NMCK, r.DateOfPlacement }).ToListAsync(cancellationToken))
            .Select(x => IdentityKey(x.Customer, x.NMCK, x.DateOfPlacement)),
            StringComparer.Ordinal);

        var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var number = row.PurchaseNumber.Trim();
            var key = IdentityKey(row.Customer, row.NMCK, row.DateOfPlacement);
            row.IsDuplicate = (number.Length > 0 && (numbers.Contains(number) || !seenNumbers.Add(number)))
                              || !seenKeys.Add(key);
        }
    }

    private static string IdentityKey(string? customer, decimal nmck, DateTime? placement) =>
        $"{customer?.Trim()}|{nmck.ToString("0.##", CultureInfo.InvariantCulture)}|{placement?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    private static void BuildStats(
        LegacyImportPreview preview,
        List<string> ignored,
        IDictionary<string, int> warnings,
        IDictionary<string, int> unknownTypes,
        IDictionary<string, int> unknownStatuses)
    {
        preview.IgnoredColumns.AddRange(ignored);

        int Filled(Func<LegacyImportRow, bool> predicate) => preview.Rows.Count(predicate);

        preview.Stats.Add(new LegacyImportStat("Заказчик", Filled(r => r.Customer.Length > 0)));
        preview.Stats.Add(new LegacyImportStat("Ссылка в «ЕИС»", Filled(r => r.EisLink is { Length: > 0 }),
            $"номер закупки извлечён у {Filled(r => r.PurchaseNumber.Length > 0)}"));
        preview.Stats.Add(new LegacyImportStat("Тип закупки", Filled(r => r.TypeText.Length > 0),
            "в справочнике не найдено — поле останется пустым"));
        preview.Stats.Add(new LegacyImportStat("Место поставки товара", Filled(r => r.PlaceOfDelivery.Length > 0)));
        preview.Stats.Add(new LegacyImportStat("Номер резерва", Filled(r => r.ReserveNumber.Length > 0)));
        preview.Stats.Add(new LegacyImportStat("Нац режим", Filled(r => r.NationalMode.Length > 0)));
        preview.Stats.Add(new LegacyImportStat("Статус резерва B2B", Filled(r => r.B2BText.Length > 0)));
        preview.Stats.Add(new LegacyImportStat("Дата передачи на размещение", Filled(r => r.DateOfTransferForPlacement is not null)));
        preview.Stats.Add(new LegacyImportStat("Дата размещения на площадке", Filled(r => r.DateOfPlacement is not null)));
        preview.Stats.Add(new LegacyImportStat("Дата торгов", Filled(r => r.BiddingDate is not null)));
        preview.Stats.Add(new LegacyImportStat("Дата подведения итогов", Filled(r => r.DateResults is not null)));
        preview.Stats.Add(new LegacyImportStat("НМЦК", Filled(r => r.NMCK > 0)));
        preview.Stats.Add(new LegacyImportStat("Наша минимальная сумма", Filled(r => r.MinPrice > 0)));
        preview.Stats.Add(new LegacyImportStat("Итоговая сумма", Filled(r => r.ResultPrice > 0)));
        preview.Stats.Add(new LegacyImportStat("Дата заключения контракта", Filled(r => r.DateOfConclusionOfTheContract is not null)));
        preview.Stats.Add(new LegacyImportStat("Сроки поставки", Filled(r => r.DeliveryTerms.Length > 0), "текстом, как в файле"));
        preview.Stats.Add(new LegacyImportStat("Примечание", Filled(r => r.Description.Length > 0)));
        preview.Stats.Add(new LegacyImportStat("Отметка «Согласован»", Filled(r => r.IsApproved)));
        preview.Stats.Add(new LegacyImportStat("Отметка «Звони»", Filled(r => r.IsCall)));
        preview.Stats.Add(new LegacyImportStat("Отметка «По заявкам»", Filled(r => r.IsByRequest)));
        preview.Stats.Add(new LegacyImportStat("Статус контракта «Подписан»", Filled(r => r.ContractStatusId == 3)));

        foreach (var pair in unknownTypes.OrderByDescending(p => p.Value))
        {
            preview.UnknownTypes.Add(new LegacyImportStat(pair.Key, pair.Value));
        }

        foreach (var pair in unknownStatuses.OrderByDescending(p => p.Value))
        {
            preview.UnknownStatuses.Add(new LegacyImportStat(pair.Key, pair.Value));
        }

        foreach (var pair in warnings.OrderByDescending(p => p.Value))
        {
            preview.Warnings.Add(new LegacyImportStat(pair.Key, pair.Value));
        }

        foreach (var group in preview.Rows.GroupBy(r => r.Stage).OrderBy(g => g.Key))
        {
            preview.Stages.Add(new LegacyImportStat(WorkflowStages.Name(group.Key), group.Count()));
        }
    }

    // -----------------------------------------------------------------------
    // Значения ячеек
    // -----------------------------------------------------------------------
    /// <summary>Текст из ячейки: единые переводы строк, без хвостов, по длине колонки.</summary>
    internal static string Clean(string value, int limit)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = value.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, "\n{3,}", "\n\n").Trim();
        return Clamp(text, limit);
    }

    private static string Clamp(string? value, int limit)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= limit ? value : value.Substring(0, limit - 1).TrimEnd() + "…";
    }

    /// <summary>ВСЕ ЗАГЛАВНЫЕ → Обычный вид; сокращения (ООО, ГБУЗ, ЦРБ…) остаются заглавными.</summary>
    internal static string SentenceCase(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count == 0)
        {
            return text;
        }

        var upper = letters.Count(char.IsUpper);
        if ((double)upper / letters.Count < 0.85)
        {
            return text;    // набрано в обычном регистре — не трогаем
        }

        var lowered = text.ToLowerInvariant();
        var words = lowered.Split(' ').Select(word =>
        {
            var stripped = word.Trim().Trim('«', '»', '"', '.', ',', ';', ':');
            if (stripped.Length is > 0 and <= 6 && Abbreviations.Contains(stripped.ToUpperInvariant()))
            {
                return word.Replace(stripped, stripped.ToUpperInvariant(), StringComparison.Ordinal);
            }

            return word;
        });

        var result = string.Join(' ', words);
        return result.Length == 0 ? result : char.ToUpperInvariant(result[0]) + result.Substring(1);
    }

    /// <summary>Читаемое имя заказчика для «Наименования»: название в кавычках, если оно есть.</summary>
    internal static string ShortCustomer(string customer, int limit = 140)
    {
        var text = Clean(customer, 1000).Replace("\n", " ");
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var match = QuotedRegex.Match(text);
        var baseName = SentenceCase(match.Success ? match.Groups[1].Value.Trim() : text).Trim();
        if (baseName.Length <= limit)
        {
            return baseName;
        }

        var cut = baseName.Substring(0, limit);
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > limit / 2)
        {
            cut = cut.Substring(0, lastSpace);
        }

        return cut.TrimEnd(' ', ',', ';', ':', '-') + "…";
    }

    /// <summary>
    /// Дата из свободной ячейки: «подписан 05.11.2025», «2.10.2025 в 12.00».
    /// Возвращает дату (со временем, если оно указано) и остаток текста без даты.
    /// </summary>
    internal static (DateTime? Value, string Leftover) ParseDate(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return (null, string.Empty);
        }

        int day, month, year, start, end;
        var match = DateRegex.Match(text);
        if (match.Success)
        {
            day = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            year = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            start = match.Index;
            end = match.Index + match.Length;
        }
        else
        {
            var iso = IsoDateRegex.Match(text);
            if (!iso.Success)
            {
                return (null, text);
            }

            year = int.Parse(iso.Groups[1].Value, CultureInfo.InvariantCulture);
            month = int.Parse(iso.Groups[2].Value, CultureInfo.InvariantCulture);
            day = int.Parse(iso.Groups[3].Value, CultureInfo.InvariantCulture);
            start = iso.Index;
            end = iso.Index + iso.Length;
        }

        if (month is < 1 or > 12 || day is < 1 or > 31 || year is < 1990 or > 2100)
        {
            return (null, text);
        }

        DateTime moment;
        try
        {
            moment = new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return (null, text);
        }

        // «Дата торгов» в сайте хранится с временем — ищем его сразу за датой.
        var time = TimeRegex.Match(text.Substring(end));
        var leftoverStart = end;
        if (time.Success)
        {
            var hour = int.Parse(time.Groups[1].Value, CultureInfo.InvariantCulture);
            var minute = int.Parse(time.Groups[2].Value, CultureInfo.InvariantCulture);
            if (hour <= 23 && minute <= 59)
            {
                moment = moment.AddHours(hour).AddMinutes(minute);
                leftoverStart = end + time.Length;
            }
        }

        var leftover = $"{text.Substring(0, start)} {text.Substring(leftoverStart)}";
        leftover = Regex.Replace(leftover, @"\s+", " ").Trim(' ', '\t', ',', ';', ':', '-', '—');
        // «подписан», «заявка», предлог «в» — служебные слова, к делу не относятся.
        leftover = Regex.Replace(leftover, @"\b(подписан\w*|заявк\w*|в)\b", " ", RegexOptions.IgnoreCase);
        leftover = Regex.Replace(leftover, @"\s+", " ").Trim(' ', '\t', ',', ';', ':', '-', '—');
        return (moment, leftover);
    }

    /// <summary>Сумма из свободной ячейки: «380000швв», «2 545 178,02», «990 000/ 61042».</summary>
    internal static (decimal? Value, bool Call) ParseMoney(string value)
    {
        var text = (value ?? string.Empty).Replace('\u00A0', ' ').Replace('\u202F', ' ').Trim();
        if (text.Length == 0)
        {
            return (null, false);
        }

        var call = text.Contains("звон", StringComparison.OrdinalIgnoreCase);
        foreach (Match match in NumberRegex.Matches(text))
        {
            var raw = match.Value.Replace(" ", string.Empty).Replace(",", ".");
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }

            // «строго 2026!» — это год, а не сумма.
            if (!raw.Contains('.') && number is >= 1990 and <= 2100)
            {
                continue;
            }

            return (Math.Round(number, 2), call);
        }

        return (null, call);
    }

    /// <summary>Номер закупки из ссылки: regNumber, purchaseNoticeNumber, идентификатор площадки.</summary>
    internal static string? ExtractNumber(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        foreach (var pattern in new[]
                 {
                     @"[?&](?:regNumber|purchaseNoticeNumber)=(\d{6,})",
                     @"[?&]noticeInfoId=(\d{6,})",
                     @"[?&]noticeGuid=([0-9a-fA-F-]{8,})",
                     @"[?&](?:id|sell|procedures)=(\d{6,})"
                 })
        {
            var match = Regex.Match(reference, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        var tail = Regex.Matches(reference, @"/(\d{6,})");
        return tail.Count > 0 ? tail[^1].Groups[1].Value : null;
    }

    // -----------------------------------------------------------------------
    // Чтение файла
    // -----------------------------------------------------------------------
    private static bool IsWorkbook(byte[] bytes, string fileName) =>
        fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        (bytes.Length > 3 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04);

    private static List<List<string>> ReadWorkbook(MemoryStream buffer)
    {
        buffer.Position = 0;
        using var workbook = new XLWorkbook(buffer);
        var sheet = workbook.Worksheet(1);
        var result = new List<List<string>>();
        foreach (var row in sheet.RowsUsed())
        {
            var cells = new List<string>();
            foreach (var cell in row.CellsUsed())
            {
                var index = cell.Address.ColumnNumber - 1;
                while (cells.Count < index)
                {
                    cells.Add(string.Empty);
                }

                cells.Add(cell.GetString());
            }

            result.Add(cells);
        }

        return result;
    }

    /// <summary>UTF-8 (с BOM и без), UTF-16, иначе Windows-1251 — так сохраняет Excel в русской локали.</summary>
    internal static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return DecodeWindows1251(bytes);
        }
    }

    private static string DecodeWindows1251(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            builder.Append(b < 0x80 ? (char)b : Windows1251UpperHalf[b - 0x80]);
        }

        return builder.ToString();
    }

    /// <summary>Разбивает CSV, сам определяя разделитель: запятая у Excel EN, «;» у русской локали.</summary>
    internal static List<List<string>> SplitCsv(string text)
    {
        var rows = SplitWith(text, ',');
        if (rows.Count > 0 && rows[0].Count < 3)
        {
            var semicolon = SplitWith(text, ';');
            if (semicolon.Count > 0 && semicolon[0].Count > rows[0].Count)
            {
                return semicolon;
            }
        }

        return rows;
    }

    private static List<List<string>> SplitWith(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    break;
                default:
                    if (ch == delimiter)
                    {
                        row.Add(cell.ToString());
                        cell.Clear();
                    }
                    else
                    {
                        cell.Append(ch);
                    }

                    break;
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Строка заголовков — та, где есть «Заказчик» и «НМЦК» (смотрим первые шесть строк).</summary>
    private static int FindHeaderRow(List<List<string>> rows)
    {
        for (var index = 0; index < Math.Min(rows.Count, 6); index++)
        {
            var keys = rows[index].Select(Normalize).ToList();
            if (keys.Contains("заказчик") && keys.Any(key => key.StartsWith("нмцк", StringComparison.Ordinal)))
            {
                return index;
            }
        }

        return -1;
    }

    private static Dictionary<int, Field> MapColumns(List<string> header, out List<string> ignored)
    {
        ignored = new List<string>();
        var mapping = new Dictionary<int, Field>();

        for (var index = 0; index < header.Count; index++)
        {
            var title = header[index].Trim();
            if (index > LastColumn)
            {
                // Всё, что правее «Примечания», по условию не берём.
                if (title.Length > 0)
                {
                    ignored.Add($"{title} — за пределами колонок A..Q");
                }

                continue;
            }

            if (index == FirstColumn)
            {
                mapping[index] = Field.SourceRef;
                continue;
            }

            var key = Normalize(title);
            if (key.Length == 0)
            {
                continue;
            }

            if (ColumnMap.TryGetValue(key, out var field))
            {
                mapping[index] = field;
            }
            else
            {
                ignored.Add($"{title} — нет такого поля в реестре");
            }
        }

        if (mapping.Values.All(field => field != Field.Customer))
        {
            ignored.Add("не найдена колонка «Заказчик»");
        }

        return mapping;
    }

    private static string Normalize(string value)
    {
        var text = (value ?? string.Empty).ToLowerInvariant().Replace('ё', 'е');
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
