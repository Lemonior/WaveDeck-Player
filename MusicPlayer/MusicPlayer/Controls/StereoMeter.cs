using System.Windows;
using System.Windows.Media;
using MusicPlayer.Services;

namespace MusicPlayer.Controls;

public sealed class StereoMeter : FrameworkElement
{
    public float Left { get; set; }
    public float Right { get; set; }
    private readonly Dictionary<string, SolidColorBrush> _colors = [];
    private Pen? _border;
    private FormattedText[]? _labels;
    private int _theme = -1;
    private double _dpi;
    protected override void OnRender(DrawingContext dc)
    {
        var half = ActualWidth / 2;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_theme != ThemeService.Revision || _dpi != dpi)
        {
            _theme = ThemeService.Revision; _dpi = dpi; _colors.Clear();
            foreach (var hex in new[] { "#233039", "#D9876B", "#F4C36D", "#B8CA87" }) _colors[hex] = ThemeService.Brush(hex);
            _border = new(ThemeService.Brush("#6A777F"), .4); _border.Freeze();
            _labels = new[] { "L", "R" }.Select(text => new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 10, Brushes.LightSlateGray, dpi)).ToArray();
        }
        for (var ear = 0; ear < 2; ear++)
        {
            var value = ear == 0 ? Left : Right; var origin = ear * half;
            dc.DrawText(_labels![ear], new Point(origin, 3));
            var level = value <= 0 ? 0 : Math.Clamp((20 * Math.Log10(value) + 48) / 48, 0, 1);
            const int segments = 25; var width = (half - 23) / segments;
            for (var i = 0; i < segments; i++)
            {
                var color = i >= level * segments ? "#233039" : i > 21 ? "#D9876B" : i > 17 ? "#F4C36D" : "#B8CA87";
                dc.DrawRectangle(_colors[color], _border, new Rect(origin + 14 + i * width, 5, Math.Max(1, width - 1), 8));
            }
        }
    }
}
