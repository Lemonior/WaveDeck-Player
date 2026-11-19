using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MusicPlayer.Services;

namespace MusicPlayer.Controls;

public sealed class Knob : RangeBase
{
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption), typeof(string), typeof(Knob), new FrameworkPropertyMetadata("Knob", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string Unit { get; set; } = "%";
    public double DefaultValue { get; set; }
    public bool Logarithmic { get; set; }
    private Point _start;
    private double _startFraction;
    private bool _dragging;
    public Knob()
    {
        Focusable = true; Cursor = Cursors.Hand; MinHeight = 58; MinWidth = 70;
        SetResourceReference(ForegroundProperty, "Gold"); SetResourceReference(BackgroundProperty, "Chrome");
        ValueChanged += (_, _) => InvalidateVisual();
        var menu = new System.Windows.Controls.ContextMenu();
        var precise = new System.Windows.Controls.MenuItem { Header = L.T("Set exact value…") };
        precise.Click += (_, _) => { var value = ValueWindow.Edit(Window.GetWindow(this), Caption, Value, Minimum, Maximum); if (value != null) SetCurrentValue(ValueProperty, value.Value); };
        var reset = new System.Windows.Controls.MenuItem { Header = L.T("Reset this knob") }; reset.Click += (_, _) => Reset();
        menu.Items.Add(precise); menu.Items.Add(reset); ContextMenu = menu;
        UiText.SetHeader(precise, "Set exact value…"); UiText.SetHeader(reset, "Reset this knob");
    }
    private double Fraction => Logarithmic ? Math.Log(Value / Minimum) / Math.Log(Maximum / Minimum) : (Value - Minimum) / (Maximum - Minimum);
    internal void MoveFraction(double fraction)
    {
        var value = Logarithmic ? Minimum * Math.Pow(Maximum / Minimum, Math.Clamp(fraction, 0, 1)) : Minimum + Math.Clamp(fraction, 0, 1) * (Maximum - Minimum);
        if (SmallChange > 0 && !Logarithmic) value = Math.Round(value / SmallChange) * SmallChange;
        SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
    }
    public void Reset() => SetCurrentValue(ValueProperty, DefaultValue);
    public string ValueLabel => Unit switch
    {
        "%" => $"{Value * 100:0}%", "dB" => $"{Value:+0.0;-0.0;0} dB", "pan" => Value == 0 ? L.T("Center") : $"{Math.Abs(Value) * 100:0}% {(Value < 0 ? "L" : "R")}",
        "Hz" => Value >= 20000 ? L.T("Open") : Value >= 1000 ? $"{Value / 1000:0.0} kHz" : $"{Value:0} Hz",
        "x" => $"{Value:0.00}×", "st" => $"{Value:+0.0;-0.0;0} st", "ms" => $"{Value:0} ms", _ => $"{Value:0.00}"
    };
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ink = Foreground; var muted = (Brush)FindResource("Muted");
        void Text(string text, double y, Brush brush)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, brush, dpi);
            dc.DrawText(formatted, new Point((ActualWidth - formatted.Width) / 2, y));
        }
        Text(L.T(Caption), 0, muted);
        var radius = Math.Max(8, Math.Min(18, (ActualHeight - 28) / 2)); var center = new Point(ActualWidth / 2, 14 + radius);
        dc.DrawEllipse(Background, new Pen(muted, IsKeyboardFocused ? 1.6 : 1), center, radius, radius);
        var angle = (-135 + Math.Clamp(Fraction, 0, 1) * 270) * Math.PI / 180;
        var end = new Point(center.X + Math.Sin(angle) * radius * .76, center.Y - Math.Cos(angle) * radius * .76);
        dc.DrawLine(new Pen(ink, 2.3), center, end);
        dc.DrawEllipse(ink, null, new Point(center.X + Math.Sin(angle) * (radius + 3), center.Y - Math.Cos(angle) * (radius + 3)), 1.4, 1.4);
        Text(ValueLabel, ActualHeight - 12, ink);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); if (!IsEnabled) return;
        Focus(); if (e.ClickCount == 2) { Reset(); e.Handled = true; return; }
        _start = e.GetPosition(this); _startFraction = Fraction; _dragging = CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (!_dragging) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ReleaseMouseCapture(); return; }
        var point = e.GetPosition(this); var precision = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .2 : 1;
        MoveFraction(_startFraction + ((_start.Y - point.Y) + (point.X - _start.X) * .3) / 130 * precision); e.Handled = true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); if (_dragging) { ReleaseMouseCapture(); e.Handled = true; } }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); _dragging = false; }
    protected override void OnMouseWheel(MouseWheelEventArgs e) { base.OnMouseWheel(e); MoveFraction(Fraction + Math.Sign(e.Delta) * .025); e.Handled = true; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Home) Reset();
        else if (e.Key is Key.Up or Key.Right) MoveFraction(Fraction + .025);
        else if (e.Key is Key.Down or Key.Left) MoveFraction(Fraction - .025);
        else return;
        e.Handled = true;
    }
}
