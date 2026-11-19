using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace WaveDeck.Linux;

internal static class LinuxText
{
    public static void Bind(AvaloniaObject target, AvaloniaProperty property, string english) => target.Bind(property,
        new Binding(nameof(L.Code)) { Source = L.Instance, Converter = new TextConverter(english), Mode = BindingMode.OneWay });
    private sealed class TextConverter(string english) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => L.T(english);
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    public sealed class ValueConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is string text ? L.T(text) : value;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
