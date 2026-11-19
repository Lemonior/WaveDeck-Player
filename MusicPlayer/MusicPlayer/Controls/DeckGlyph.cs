using System.Windows;
using System.Windows.Media;

namespace MusicPlayer.Controls;

/// <summary>Resolution-independent early desktop icon strokes.</summary>
public class DeckGlyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(DeckGlyph), new FrameworkPropertyMetadata("Play", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(DeckGlyph), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    internal static Geometry Shape(string kind) => Geometry.Parse(kind switch
    {
        "Minimize" => "M3,12 L13,12",
        "Maximize" => "M3,3 L13,3 13,13 3,13 Z M3,5 L13,5",
        "Restore" => "M5,3 L13,3 13,11 M3,5 L11,5 11,13 3,13 Z",
        "Close" => "M4,4 L12,12 M12,4 L4,12",
        "Help" => "M6,5 C6,2 12,2 12,5 C12,7 8,7 8,9 M8,12 L8,13",
        "Pause" => "M5,3 L5,13 M11,3 L11,13",
        "Previous" => "M3,3 L3,13 M12,3 L5,8 12,13 Z",
        "Next" => "M13,3 L13,13 M4,3 L11,8 4,13 Z",
        "Stop" => "M4,4 L12,4 12,12 4,12 Z",
        _ => "M5,3 L13,8 5,13 Z"
    });
    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 16, size / 16));
        var pen = new Pen(Ink, Kind == "Pause" ? 2.8 : 1.5) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square, LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(Kind is "Play" or "Stop" ? Ink : null, pen, Shape(Kind)); dc.Pop(); dc.Pop();
    }
    internal static DrawingImage TaskbarImage(string kind)
    { var image = new DrawingImage(new GeometryDrawing(Brushes.White, new Pen(Brushes.White, kind == "Pause" ? 2.8 : 1.5), Shape(kind))); image.Freeze(); return image; }
}
