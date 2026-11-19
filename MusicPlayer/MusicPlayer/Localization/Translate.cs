using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MusicPlayer.Services;

namespace MusicPlayer.Localization;

// Explicit attached properties keep translation away from editable fields and media metadata.
public static class Translate
{
    public static readonly DependencyProperty TextProperty = Register("Text");
    public static readonly DependencyProperty ContentProperty = Register("Content");
    public static readonly DependencyProperty HeaderProperty = Register("Header");
    public static readonly DependencyProperty TitleProperty = Register("Title");
    public static readonly DependencyProperty ToolTipProperty = Register("ToolTip");
    public static readonly DependencyProperty CaptionProperty = Register("Caption");
    private static DependencyProperty Register(string name) => DependencyProperty.RegisterAttached(name, typeof(string), typeof(Translate), new PropertyMetadata(null, Changed));
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        var property = DependencyPropertyDescriptor.FromName(e.Property.Name, target.GetType(), target.GetType())?.DependencyProperty;
        if (property != null && e.NewValue is string english) Bind(target, property, english);
    }
    public static void Bind(DependencyObject target, DependencyProperty property, string english) => BindingOperations.SetBinding(target, property,
        new Binding(nameof(Services.Localization.Code)) { Source = Services.Localization.Instance, Converter = new TextConverter(english), Mode = BindingMode.OneWay });
    public static string GetText(DependencyObject target) => (string)target.GetValue(TextProperty);
    public static void SetText(DependencyObject target, string value) => target.SetValue(TextProperty, value);
    public static string GetContent(DependencyObject target) => (string)target.GetValue(ContentProperty);
    public static void SetContent(DependencyObject target, string value) => target.SetValue(ContentProperty, value);
    public static string GetHeader(DependencyObject target) => (string)target.GetValue(HeaderProperty);
    public static void SetHeader(DependencyObject target, string value) => target.SetValue(HeaderProperty, value);
    public static string GetTitle(DependencyObject target) => (string)target.GetValue(TitleProperty);
    public static void SetTitle(DependencyObject target, string value) => target.SetValue(TitleProperty, value);
    public static string GetToolTip(DependencyObject target) => (string)target.GetValue(ToolTipProperty);
    public static void SetToolTip(DependencyObject target, string value) => target.SetValue(ToolTipProperty, value);
    public static string GetCaption(DependencyObject target) => (string)target.GetValue(CaptionProperty);
    public static void SetCaption(DependencyObject target, string value) => target.SetValue(CaptionProperty, value);
    private sealed class TextConverter(string english) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Services.Localization.T(english);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    public static void LocalizeItems(ComboBox box)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        var binding = new MultiBinding { Converter = new ItemConverter() };
        binding.Bindings.Add(new Binding());
        binding.Bindings.Add(new Binding(nameof(Services.Localization.Code)) { Source = Services.Localization.Instance });
        text.SetBinding(TextBlock.TextProperty, binding);
        box.ItemTemplate = new DataTemplate { VisualTree = text };
    }
    private sealed class ItemConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values[0] is string text ? Services.Localization.T(text) : "";
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
