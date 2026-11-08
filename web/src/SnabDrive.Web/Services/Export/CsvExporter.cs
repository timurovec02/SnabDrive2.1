using System.Globalization;
using System.Text;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services.Export;

/// <summary>Выгрузка текущей выборки реестра в CSV (открывается в Excel).</summary>
public static class CsvExporter
{
    public static string Export(IEnumerable<RegistryRowDto> rows, IReadOnlyList<RegistryColumn> columns)
    {
        var builder = new StringBuilder();

        // BOM, чтобы Excel корректно определил UTF-8.
        builder.Append('\uFEFF');

        builder.AppendLine(string.Join(';', columns.Select(c => Escape(c.Title))));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(';', columns.Select(c => Escape(GetValue(row, c)))));
        }

        return builder.ToString();
    }

    internal static string GetValue(RegistryRowDto row, RegistryColumn column) => column.Key switch
    {
        "NameLink" => row.NameLink,
        "PurchaseNumber" => row.PurchaseNumber,
        "Customer" => row.Customer,
        "TypeOfPurchaseId" => row.TypeOfPurchaseName ?? string.Empty,
        "PlaceOfDelivery" => row.PlaceOfDelivery,
        "ReserveNumber" => row.ReserveNumber,
        "NationalMode" => row.NationalMode,
        "BiddingDate" => FormatDateTime(row.BiddingDate),
        "ApplicationEndDate" => FormatDateTime(row.ApplicationEndDate),
        "DateOfTransferForPlacement" => FormatDateTime(row.DateOfTransferForPlacement),
        "DateOfPlacement" => FormatDateTime(row.DateOfPlacement),
        "DateResults" => FormatDate(row.DateResults),
        "DateOfConclusionOfTheContract" => FormatDate(row.DateOfConclusionOfTheContract),
        "ArchivateDate" => FormatDate(row.ArchivateDate),
        "NMCK" => FormatMoney(row.NMCK),
        "MinPrice" => FormatMoney(row.MinPrice),
        "ResultPrice" => FormatMoney(row.ResultPrice),
        "DeliveryDate" => FormatDate(row.DeliveryDate),
        "Description" => row.Description,
        "Note" => row.Note,
        "B2BStatusId" => row.B2BStatusName ?? string.Empty,
        "ExecutionStatusId" => row.ExecutionStatusName ?? string.Empty,
        "IsFinished" => row.IsFinished == true ? "да" : "нет",
        "IsApproved" => row.IsApproved ? "да" : "нет",
        "Stage" => row.StageName,
        "StageCompletedBy" => row.StageCompletedBy ?? string.Empty,
        "StageCompletedAtUtc" => FormatDateTime(row.StageCompletedAtUtc),
        _ => string.Empty
    };

    private static string FormatDate(DateTime? value) => value?.ToString("dd.MM.yyyy") ?? string.Empty;

    private static string FormatDateTime(DateTime? value) => value?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;

    private static string FormatMoney(decimal value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Экранирование значения для CSV.
    /// Кроме кавычек и разделителей закрываем инъекцию формул: ячейка, начинающаяся со знака
    /// формулы (=, +, @, табуляция, перевод строки), в Excel исполняется как формула, поэтому
    /// перед ней ставится апостроф и значение остаётся текстом. Иначе пользователь с правом
    /// редактировать реестр мог бы «подложить» DDE-формулу (=cmd|...) тому, кто откроет выгрузку.
    /// </summary>
    internal static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"");

        var first = escaped[0];
        var looksLikeFormula = first is '=' or '+' or '@' or '\t' or '\r'
                               || (first == '-' && !IsPlainNumber(escaped));
        if (looksLikeFormula)
        {
            escaped = "'" + escaped;
        }

        var needsQuotes = escaped.Contains(';') || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r')
                          || escaped.StartsWith('\'');
        return needsQuotes ? $"\"{escaped}\"" : escaped;
    }

    /// <summary>Минус в начале допустим у отрицательных чисел — их апострофом не ломаем.</summary>
    private static bool IsPlainNumber(string value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
}
