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
        "Customer" => row.Customer,
        "TypeOfPurchaseId" => row.TypeOfPurchaseName ?? string.Empty,
        "PlaceOfDelivery" => row.PlaceOfDelivery,
        "ReserveNumber" => row.ReserveNumber,
        "NationalMode" => row.NationalMode,
        "BiddingDate" => FormatDate(row.BiddingDate),
        "DateOfTransferForPlacement" => FormatDate(row.DateOfTransferForPlacement),
        "DateOfPlacement" => FormatDate(row.DateOfPlacement),
        "DateResults" => FormatDate(row.DateResults),
        "DateOfConclusionOfTheContract" => FormatDate(row.DateOfConclusionOfTheContract),
        "ArchivateDate" => FormatDate(row.ArchivateDate),
        "NMCK" => FormatMoney(row.NMCK),
        "MinPrice" => FormatMoney(row.MinPrice),
        "ResultPrice" => FormatMoney(row.ResultPrice),
        "Winner" => row.Winner,
        "DeliveryTime" => row.DeliveryTime,
        "Description" => row.Description,
        "Note" => row.Note,
        "B2BStatusId" => row.B2BStatusName ?? string.Empty,
        "ExecutionStatusId" => row.ExecutionStatusName ?? string.Empty,
        "IsFinished" => row.IsFinished == true ? "да" : "нет",
        _ => string.Empty
    };

    private static string FormatDate(DateTime? value) => value?.ToString("dd.MM.yyyy") ?? string.Empty;

    private static string FormatMoney(decimal value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuotes = value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{escaped}\"" : escaped;
    }
}
