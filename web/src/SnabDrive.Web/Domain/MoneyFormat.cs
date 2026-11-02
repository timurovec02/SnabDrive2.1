using System.Globalization;

namespace SnabDrive.Web.Domain;

/// <summary>
/// Единый формат сумм. Реестр и обе таблицы Контур показывают деньги одинаково,
/// поэтому формат объявлен здесь, а не в каждой странице отдельно.
/// </summary>
public static class MoneyFormat
{
    /// <summary>Как в реестре: ноль показываем прочерком, разделитель тысяч — запятая.</summary>
    public static string Money(decimal value) =>
        value == 0 ? "—" : value.ToString("#,0.##", CultureInfo.InvariantCulture);
}
