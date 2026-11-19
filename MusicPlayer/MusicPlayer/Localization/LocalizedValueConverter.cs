using System.Globalization;
using System.Windows.Data;

namespace MusicPlayer.Localization;

public sealed class LocalizedValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string text ? L.T(text) : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
