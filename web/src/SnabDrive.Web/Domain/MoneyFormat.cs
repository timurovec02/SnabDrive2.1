using System.Globalization;

namespace SnabDrive.Web.Domain;

/// <summary>
/// Единый формат сумм: 000 000 000.00 — разделитель тысяч пробел, копейки через точку.
/// Реестр, «Задачи» и обе таблицы Контур показывают деньги одинаково.
/// </summary>
public static class MoneyFormat
{
    private static readonly NumberFormatInfo Nfi = CreateFormat();

    private static NumberFormatInfo CreateFormat()
    {
        var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        nfi.NumberGroupSeparator = " ";
        nfi.NumberDecimalSeparator = ".";
        nfi.NumberGroupSizes = new[] { 3 };
        return nfi;
    }

    /// <summary>Ноль показываем прочерком, иначе — 000 000 000.00.</summary>
    public static string Money(decimal value) => value == 0 ? "\u2014" : MoneyPlain(value);

    /// <summary>Тот же шаблон без прочерка: 2 545 178.02.</summary>
    public static string MoneyPlain(decimal value) => value.ToString("#,##0.00", Nfi);

    /// <summary>Понимает «2 545 178.02», «2545178,02» и «2545178.02».</summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Replace("\u00A0", string.Empty).Replace("\u202F", string.Empty)
            .Replace(" ", string.Empty).Replace(",", ".");
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
