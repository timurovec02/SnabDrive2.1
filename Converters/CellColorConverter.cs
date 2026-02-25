using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace SnabDrive2._0.Converters
{
    public class CellColorConverter : IValueConverter
    {
        // Статический словарь для доступа из конвертера
        public static Dictionary<(int id, string column), string> CellColors { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                // value - это объект строки (Regedit или ArchiveRegedit)
                // parameter - имя колонки
                if (value == null || parameter == null)
                    return Brushes.Transparent;

                // Определяем тип и ID записи
                int recordId = 0;
                if (value is Regedit reg)
                    recordId = reg.Id;
                else if (value is ArchiveRegedit arch)
                    recordId = arch.IdOld;
                else
                    return Brushes.Transparent;

                string columnName = parameter.ToString();

                // Ищем цвет в словаре
                if (CellColors != null && CellColors.TryGetValue((recordId, columnName), out string colorCode))
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorCode));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CellColorConverter error: {ex.Message}");
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
