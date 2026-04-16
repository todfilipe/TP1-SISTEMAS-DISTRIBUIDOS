using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OneHealthMonitor.Converters
{
    /// <summary>
    /// Converte estado do sensor para SolidColorBrush.
    /// ativo→#4CAF50, manutencao→#FFC107, desativado→#EF5350
    /// indisponivel→#FF9800, desligado→#607D8B
    /// </summary>
    public class StateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string estado = (value as string)?.ToLower() ?? "";
            string hex = estado switch
            {
                "ativo" => "#4CAF50",
                "manutencao" => "#FFC107",
                "desativado" => "#EF5350",
                "indisponivel" => "#FF9800",
                "desligado" => "#607D8B",
                _ => "#607D8B"
            };
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
