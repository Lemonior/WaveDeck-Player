using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Layout;
using MusicPlayer.Audio;

namespace WaveDeck.Linux;

internal sealed class DeckSlider : Slider
{
    protected override Type StyleKeyOverride => typeof(Slider);
    public DeckSlider() { Template = new Avalonia.Controls.Templates.FuncControlTemplate<Slider>((_, _) => new Border { Background=Brushes.Transparent }); PropertyChanged += (_,e) => { if(e.Property==ValueProperty||e.Property==MaximumProperty||e.Property==MinimumProperty)InvalidateVisual(); }; }
    public override void Render(DrawingContext context)
    {
        base.Render(context); var vertical=Orientation==Orientation.Vertical;var length=vertical?Bounds.Height:Bounds.Width;
        if(length<22)return;var ratio=(Value-Minimum)/Math.Max(.0001,Maximum-Minimum);var center=vertical?new Point(Bounds.Width/2,10+(1-ratio)*(length-20)):new Point(10+ratio*(length-20),Bounds.Height/2);
        var rail=vertical?new Rect(Bounds.Width/2-2,10,4,length-20):new Rect(10,Bounds.Height/2-2,length-20,4);
        var line=new Pen(new SolidColorBrush(DeckTheme.Line),1);context.DrawRectangle(DeckTheme.Panel,line,rail);
        var fill=vertical?new Rect(rail.X,center.Y,4,Math.Max(0,rail.Bottom-center.Y)):new Rect(rail.X,rail.Y,Math.Max(0,center.X-rail.X),4);context.DrawRectangle(DeckTheme.Accent,null,fill);
        var knob=vertical?new Rect(center.X-9,center.Y-5,18,10):new Rect(center.X-6,center.Y-9,12,18);context.DrawRectangle(DeckTheme.Surface,new Pen(DeckTheme.Accent,1),knob,2,2);
        if(vertical)context.DrawLine(new Pen(DeckTheme.Accent,1),new(center.X-4,center.Y),new(center.X+4,center.Y));else context.DrawLine(new Pen(DeckTheme.Accent,1),new(center.X,center.Y-4),new(center.X,center.Y+4));
    }
    protected override void OnKeyDown(KeyEventArgs e){switch(e.Key){case Key.Left:case Key.Down:Value=Math.Max(Minimum,Value-SmallChange);break;case Key.Right:case Key.Up:Value=Math.Min(Maximum,Value+SmallChange);break;case Key.Home:Value=Minimum;break;case Key.End:Value=Maximum;break;default:base.OnKeyDown(e);return;}e.Handled=true;}
    private IPointer? _drag;
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    { if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { base.OnPointerPressed(e); return; } Focus(); _drag = e.Pointer; e.Pointer.Capture(this); Move(e); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag == e.Pointer) { Move(e); e.Handled = true; } else base.OnPointerMoved(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { if (_drag == e.Pointer) { Move(e); _drag = null; e.Pointer.Capture(null); e.Handled = true; } else base.OnPointerReleased(e); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { _drag = null; base.OnPointerCaptureLost(e); }
    private void Move(PointerEventArgs e) { var p = e.GetPosition(this); var ratio = Orientation == Orientation.Vertical ? 1 - (p.Y - 10) / Math.Max(1, Bounds.Height - 20) : (p.X - 10) / Math.Max(1, Bounds.Width - 20); Value = Minimum + Math.Clamp(ratio, 0, 1) * (Maximum - Minimum); }
}
internal static class DeckTheme
{
    public static string Name = "Modern";
    public static IBrush Accent = Brushes.Goldenrod, Text = Brushes.AntiqueWhite, Muted = Brushes.Gray, Panel = Brushes.Black, Surface = Brushes.Black;
    public static Color Line;
    public static Pen BorderPen = new(Brushes.Gray,1), AccentPen = new(Brushes.Gold,1);
    private static WriteableBitmap? _texture;
    public static void Apply(Window window, string name)
    {
        Name = name;
        var old = name == "OldSchool"; var retro = name == "Retro";
        var background = Color.Parse(old ? "#191611" : retro ? "#191327" : "#202930");
        Accent = new SolidColorBrush(Color.Parse(old ? "#D9B86E" : retro ? "#FF9ACF" : "#F4C36D"));
        window.Resources["SystemAccentColor"] = ((SolidColorBrush)Accent).Color;
        window.Resources["SystemAccentColorLight1"] = ((SolidColorBrush)Accent).Color;
        window.Resources["SystemAccentColorDark1"] = ((SolidColorBrush)Accent).Color;
        Text = new SolidColorBrush(Color.Parse(old ? "#E0D8C8" : retro ? "#EDE7FF" : "#DDE3E8"));
        Muted = new SolidColorBrush(Color.Parse(old ? "#B5A17D" : retro ? "#B4A2CD" : "#95A4B0"));
        Line = Color.Parse(old ? "#9A814F" : retro ? "#755587" : "#626F7C");
        BorderPen=new(new SolidColorBrush(Line),1);AccentPen=new(Accent,1);
        Panel = new SolidColorBrush(Color.Parse(old ? "#14120E" : retro ? "#21162E" : "#15212A"));
        _texture = Noise(background); Surface = new ImageBrush(_texture) { TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 96, 96, RelativeUnit.Absolute), Stretch = Stretch.None };
        window.Background = Surface; window.Foreground = Text; window.FontFamily = new(old ? "DejaVu Serif" : "DejaVu Sans");
        window.Styles.Clear();
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(ListBox), typeof(MenuItem) })
        {
            var style = new Style(x => x.OfType(type)); style.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Text)); style.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, type == typeof(Button) ? Surface : Panel)); style.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, new SolidColorBrush(Line))); style.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1))); window.Styles.Add(style);
        }
        var hover = new Style(x => x.OfType<Button>().Class(":pointerover")); hover.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, Accent)); window.Styles.Add(hover);
        var thumb = new Style(x => x.OfType<Avalonia.Controls.Primitives.Thumb>()); thumb.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Accent)); window.Styles.Add(thumb);
        var scroll = new Style(x => x.OfType<Avalonia.Controls.Primitives.ScrollBar>()); scroll.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Panel)); window.Styles.Add(scroll);
    }
    private static unsafe WriteableBitmap Noise(Color color)
    {
        var bitmap = new WriteableBitmap(new PixelSize(96,96), new Vector(96,96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var pixels = bitmap.Lock(); uint random = 0x2a794bc3;
        for (var y = 0; y < 96; y++) for (var x = 0; x < 96; x++) { random ^= random << 13; random ^= random >> 17; random ^= random << 5; var n = (int)(random % 7) - 3; var p = (byte*)pixels.Address + y * pixels.RowBytes + x * 4; p[0] = (byte)Math.Clamp(color.B+n,0,255); p[1]=(byte)Math.Clamp(color.G+n,0,255); p[2]=(byte)Math.Clamp(color.R+n,0,255); p[3]=255; }
        return bitmap;
    }
}
internal sealed class Channels : Control
{
    public AudioSnapshot? Snapshot;
    private string? _theme;
    private FormattedText[]? _captions;
    public override void Render(DrawingContext context)
    {
        base.Render(context); var width = Bounds.Width; var height = Bounds.Height / 3; if (width < 10 || height < 20) return;
        var line = DeckTheme.BorderPen; var accent = DeckTheme.AccentPen;
        if(_theme!=DeckTheme.Name+L.Instance.Code){_theme=DeckTheme.Name+L.Instance.Code;_captions=new[]{"CHANNEL A / OSCILLOSCOPE","CHANNEL B / FREQUENCY SPECTRUM","CHANNEL C / STEREO PHASE"}.Select(text=>new FormattedText(L.T(text),System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Typeface.Default,9,DeckTheme.Accent)).ToArray();}
        for (var mode=0; mode<3; mode++)
        {
            var top = mode * height; context.DrawRectangle(DeckTheme.Panel, line, new Rect(.5,top+.5,width-1,height-4));
            context.DrawText(_captions![mode], new Point(7,top+4));
            var area = new Rect(7,top+21,width-14,height-29); using var clip = context.PushClip(area);
            for (var x=area.X; x<area.Right; x+=22) context.DrawLine(line,new(x,area.Y),new(x,area.Bottom));
            for (var y=area.Y; y<area.Bottom; y+=Math.Max(1,area.Height/3)) context.DrawLine(line,new(area.X,y),new(area.Right,y));
            if (mode==1) { for (var i=0;i<64;i++) { var h = (Snapshot?.Spectrum[i]??0)*area.Height; context.DrawRectangle(DeckTheme.Accent,null,new Rect(area.X+i*area.Width/64,area.Bottom-Math.Max(1,h),Math.Max(1,area.Width/64-1),Math.Max(1,h))); } }
            else
            {
                var samples=Snapshot?.Left; var geometry=new StreamGeometry(); using(var path=geometry.Open())
                {
                    for(var i=0;i<256;i++) { var l=samples?[i*8]??0; var r=Snapshot?.Right[i*8]??0; var point=mode==0?new Point(area.X+i*area.Width/255,area.Center.Y-l*area.Height*.48):new Point(area.Center.X+(l-r)*area.Width*.3,area.Center.Y-(l+r)*area.Height*.3); if(i==0) path.BeginFigure(point,false);else path.LineTo(point); } path.EndFigure(false);
                }
                context.DrawGeometry(null,accent,geometry);
            }
        }
    }
}
