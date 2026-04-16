using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OneHealthMonitor.Converters
{
    /// <summary>
    /// Converte tipo de dado para SolidColorBrush.
    /// TEMP→#FF8C00, HUM→#2196F3, AR→#4CAF50, RUIDO→#9C27B0,
    /// PM2.5→#EF5350, PM10→#F44336, LUZ→#FFC107, VIDEO→#00BCD4
    /// </summary>
    public class DataTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string tipo = (value as string)?.ToUpper() ?? "";
            string hex = tipo switch
            {
                "TEMP" => "#FF8C00",
                "HUM" => "#2196F3",
                "AR" => "#4CAF50",
                "RUIDO" => "#9C27B0",
                "PM2.5" => "#EF5350",
                "PM10" => "#F44336",
                "LUZ" => "#FFC107",
                "VIDEO" => "#00BCD4",
                _ => "#00BFA5"
            };
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
