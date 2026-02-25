using System.Globalization;
using System.Windows.Data;
namespace SnabDrive2._0.Converters
{
    public class DateOneDayAwayConverter : IValueConverter
    {
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime date)
            {
                var today = DateTime.Today;
                var startDate = today.AddDays(0);
                var endDate = today.AddDays(1);

                return date >= startDate && date <= endDate;
            }
            return false;
        }



        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
        
    }
}


