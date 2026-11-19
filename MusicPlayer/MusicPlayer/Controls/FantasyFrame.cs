using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MusicPlayer.Services;

namespace MusicPlayer.Controls;

public sealed class FantasyFrame : Border
{
    public static readonly DependencyProperty IsOrnateProperty = DependencyProperty.Register(nameof(IsOrnate), typeof(bool), typeof(FantasyFrame), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool IsOrnate { get => (bool)GetValue(IsOrnateProperty); set => SetValue(IsOrnateProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!IsOrnate || ActualWidth < 12 || ActualHeight < 12) return;
        // Original, restrained gilded frame geometry, with no borrowed game artwork.
        var dark = new Pen(new SolidColorBrush(Color.FromRgb(77, 63, 39)), 3);
        var gold = new Pen(new SolidColorBrush(Color.FromRgb(169, 143, 86)), 1);
        var inner = new Pen(new SolidColorBrush(Color.FromRgb(87, 75, 49)), 1);
        var highlight = new Pen(new SolidColorBrush(Color.FromRgb(208, 184, 123)), .8);
        dc.DrawRoundedRectangle(null, dark, new Rect(1.5, 1.5, ActualWidth - 3, ActualHeight - 3), 2, 2);
        dc.DrawRoundedRectangle(null, gold, new Rect(1.5, 1.5, ActualWidth - 3, ActualHeight - 3), 2, 2);
        dc.DrawRectangle(null, inner, new Rect(4.5, 4.5, ActualWidth - 9, ActualHeight - 9));
        foreach (var (x, y, sx, sy) in new[] { (5d, 5d, 1d, 1d), (ActualWidth - 5, 5d, -1d, 1d), (5d, ActualHeight - 5, 1d, -1d), (ActualWidth - 5, ActualHeight - 5, -1d, -1d) })
        {
            dc.DrawLine(highlight, new Point(x, y), new Point(x + 10 * sx, y));
            dc.DrawLine(highlight, new Point(x, y), new Point(x, y + 10 * sy));
        }
    }
}
