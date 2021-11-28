using System;
using System.Windows.Data;

namespace BTrader.UI.Views
{
    public class DateTimeConverter : IValueConverter
    {
        public string Format { get; set; } = "dd/MM/yyyy HH:mm";
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value != null)
            {
                DateTime test = (DateTime)DateTime.SpecifyKind(DateTime.Parse(value.ToString()), DateTimeKind.Utc).ToLocalTime();
                string date = test.ToString(this.Format);
                return (date);
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    public class TimeSpanConverter : IValueConverter
    {
        public string Format { get; set; } = @"dd\.hh\:mm\:ss";
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value != null)
            {
                var timeSpan = (TimeSpan)value;
                var result = timeSpan.ToString(this.Format);
                return result;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
