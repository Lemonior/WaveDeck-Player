using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MusicPlayer.Audio;
using MusicPlayer.Services;

namespace MusicPlayer.Controls;

public sealed class ChannelView : FrameworkElement
{
    public string Mode { get; set; } = "Waveform";
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption), typeof(string), typeof(ChannelView), new FrameworkPropertyMetadata("CHANNEL A · WAVEFORM", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Color), typeof(ChannelView), new FrameworkPropertyMetadata(Color.FromRgb(185, 206, 116), FrameworkPropertyMetadataOptions.AffectsRender));
    public Color Accent { get => (Color)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public AudioSnapshot? Snapshot { get; set; }
    public bool Enabled { get; set; } = true;
    public string AmbientStyle { get; set; } = "Aurora";
    public bool Trails { get; set; } = true;
    public bool ColorCycle { get; set; } = true;
    private const int WavePoints = 101, SpectrumOffset = WavePoints * 3;
    private readonly float[][] _audioFrames = Enumerable.Range(0, 7).Select(_ => new float[SpectrumOffset + 64 + 1]).ToArray();
    private int _frameIndex = -1, _frameCount;
    private readonly SolidColorBrush[] _ambientBrushes = Enumerable.Range(0, 7).Select(_ => new SolidColorBrush()).ToArray();
    private readonly Pen[] _ambientPens;
    public ChannelView() { _ambientPens = _ambientBrushes.Select(brush => new Pen(brush, 1.4)).ToArray(); }
    private readonly float[] _smoothed = new float[64], _peaks = new float[64];
    private readonly Dictionary<(Brush, double), Pen> _pens = [];
    private string? _drawCaption;
    private int _drawTheme = -1;
    private Color _drawAccent;
    private double _drawDpi;
    private SolidColorBrush? _accentBrush;
    private FormattedText? _captionText;
    private Pen P(Brush brush, double width) { if (!_pens.TryGetValue((brush, width), out var pen)) { pen = new(brush, width); pen.Freeze(); _pens[(brush, width)] = pen; } return pen; }
    private static float Peak(float[] samples) { var peak = 0f; foreach (var sample in samples) peak = Math.Max(peak, Math.Abs(sample)); return peak; }
    public void UpdateAudioFrame(AudioSnapshot snapshot, bool active, double? elapsedSeconds = null)
    {
        Snapshot = snapshot;
        if (!Enabled || !active || Mode != "Ambient") return;
        var power = 0d; var peak = 0f;
        for (var i = 0; i < snapshot.Left.Length; i++)
        {
            var l = snapshot.Left[i]; var r = snapshot.Right[i];
            power += ((double)l * l + (double)r * r) * .5;
            peak = Math.Max(peak, Math.Max(Math.Abs(l), Math.Abs(r)));
        }
        var rms = Math.Sqrt(power / Math.Max(1, snapshot.Left.Length));
        _frameIndex = (_frameIndex + 1) % _audioFrames.Length; _frameCount = Math.Min(_audioFrames.Length, _frameCount + 1);
        var frame = _audioFrames[_frameIndex];
        // Preserve the recording's dynamics while making ordinary music clearly visible.
        var scale = .85 / Math.Max(.25, peak);
        for (var point = 0; point < WavePoints; point++)
        {
            var start = point * snapshot.Left.Length / WavePoints;
            var end = Math.Max(start + 1, (point + 1) * snapshot.Left.Length / WavePoints);
            double left = 0, right = 0, envelope = 0;
            for (var i = start; i < end; i++)
            {
                var l = snapshot.Left[i]; var r = snapshot.Right[i];
                left += l; right += r; envelope += ((double)l * l + (double)r * r) * .5;
            }
            frame[point] = (float)(left / (end - start) * scale);
            frame[WavePoints + point] = (float)(right / (end - start) * scale);
            frame[WavePoints * 2 + point] = (float)(Math.Sqrt(envelope / (end - start)) * scale);
        }
        snapshot.Spectrum.CopyTo(frame, SpectrumOffset);
        frame[^1] = (float)Math.Clamp(Math.Sqrt(rms) * 1.7, 0, 1);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth; var h = ActualHeight;
        if (w < 1 || h < 1) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var caption = Caption + (Enabled ? "" : "  · OFF");
        if (_drawTheme != ThemeService.Revision || _drawAccent != Accent || _drawCaption != caption || _drawDpi != dpi)
        {
            _pens.Clear(); _drawTheme = ThemeService.Revision; _drawAccent = Accent; _drawCaption = caption; _drawDpi = dpi;
            _accentBrush = new(Accent); _accentBrush.Freeze();
            _captionText = new(caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, _accentBrush, dpi);
        }
        dc.DrawRectangle(Brush("#101C22"), P(Brush("#57646D"), 1), new Rect(0.5, 0.5, w - 1, h - 1));
        var accent = _accentBrush!;
        dc.DrawText(_captionText!, new Point(7, 3));
        if (!Enabled) return;
        var top = 19.0; var bottom = h - 6; var mid = (top + bottom) / 2; var height = bottom - top;
        var grid = P(Brush("#263A43"), 0.5);
        for (var x = 8.0; x < w - 7; x += 21) dc.DrawLine(grid, new Point(x, top), new Point(x, bottom));
        for (var y = top; y <= bottom; y += height / 3) dc.DrawLine(grid, new Point(7, y), new Point(w - 7, y));
        if (Mode == "Ambient") { DrawAmbient(dc, new Rect(7, top, w - 14, Math.Max(1, height))); return; }
        if (Mode == "Spectrum")
        {
            var bw = (w - 16) / 64;
            for (var i = 0; i < 64; i++)
            {
                var value = Snapshot?.Spectrum[i] ?? 0;
                _smoothed[i] = Math.Max(value, _smoothed[i] * 0.83f); _peaks[i] = Math.Max(_smoothed[i], _peaks[i] - 0.018f);
                var bh = Math.Max(1, _smoothed[i] * height);
                dc.DrawRectangle(accent, null, new Rect(8 + i * bw, bottom - bh, Math.Max(1, bw - 1.5), bh));
                dc.DrawLine(P(Brush("#D7EAED"), 1), new Point(8 + i * bw, bottom - _peaks[i] * height), new Point(8 + i * bw + bw - 1.5, bottom - _peaks[i] * height));
            }
        }
        else
        {
            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                if (Mode == "Phase" && Snapshot != null)
                {
                    var normalization = 0.85 / Math.Max(0.04, Math.Max(Peak(Snapshot.Left), Peak(Snapshot.Right)));
                    var scale = Math.Min(w * 0.35, height * 0.8) * normalization;
                    for (var i = 0; i < Snapshot.Left.Length; i += 4)
                    {
                        var x = (Snapshot.Left[i] - Snapshot.Right[i]) * 0.707f; var y = (Snapshot.Left[i] + Snapshot.Right[i]) * 0.707f;
                        var point = new Point(w / 2 + x * scale, mid - y * height * 0.4 * normalization);
                        if (i == 0) context.BeginFigure(point, false, false); else context.LineTo(point, true, false);
                    }
                }
                else
                {
                    var samples = Snapshot?.Left; var points = (int)Math.Max(1, Math.Min(500, w - 14));
                    var normalization = samples == null ? 1 : 0.85 / Math.Max(0.04, Peak(samples));
                    for (var i = 0; i <= points; i++)
                    {
                        var sample = samples == null ? 0 : samples[Math.Min(samples.Length - 1, i * samples.Length / points)];
                        var point = new Point(7 + i * (w - 14) / points, mid - sample * height * 0.46 * normalization);
                        if (i == 0) context.BeginFigure(point, false, false); else context.LineTo(point, true, false);
                    }
                }
            }
            path.Freeze(); dc.PushClip(new RectangleGeometry(new Rect(7, top, w - 14, height))); dc.DrawGeometry(null, P(accent, 1.1), path); dc.Pop();
        }
    }
    private void DrawAmbient(DrawingContext dc, Rect bounds)
    {
        var layers = Trails ? _frameCount : Math.Min(1, _frameCount);
        dc.PushClip(new RectangleGeometry(bounds));
        for (var layer = layers - 1; layer >= 0; layer--)
        {
            var frame = _audioFrames[(_frameIndex - layer + _audioFrames.Length) % _audioFrames.Length];
            double weighted = 0, weight = 0;
            for (var bin = 0; bin < 64; bin++) { var value = frame[SpectrumOffset + bin]; weighted += value * bin; weight += value; }
            var balance = weight > .001 ? weighted / weight / 63 : 0;
            var energy = frame[^1];
            byte Blend(byte original, double value) => (byte)Math.Clamp(ColorCycle ? original * .45 + value * 140 : original, 0, 255);
            _ambientBrushes[layer].Color = Color.FromArgb((byte)(240 - layer * 30), Blend(Accent.R, 1 - balance), Blend(Accent.G, energy), Blend(Accent.B, balance));
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                var halves = AmbientStyle == "Aurora" ? 2 : 1;
                for (var half = 0; half < halves; half++)
                for (var i = 0; i < WavePoints; i++)
                {
                    var u = i / (double)(WavePoints - 1); Point point;
                    var bin = Math.Min(63, (int)(u * 63)); var spectral = frame[SpectrumOffset + bin];
                    if (AmbientStyle == "Orbit")
                    {
                        // Polar projection of the measured envelope and frequency spectrum.
                        var angle = u * Math.PI * 2; var radius = Math.Clamp(frame[WavePoints * 2 + i] * .6 + spectral * energy * .4, 0, 1);
                        point = new(bounds.X + bounds.Width * (.5 + radius * .48 * Math.Cos(angle)), bounds.Y + bounds.Height * (.5 + radius * .48 * Math.Sin(angle)));
                    }
                    else if (AmbientStyle == "Aurora")
                        point = new(bounds.X + u * bounds.Width, bounds.Y + bounds.Height * (.5 + (half == 0 ? -1 : 1) * spectral * energy * .47));
                    else
                        point = new(bounds.X + u * bounds.Width, bounds.Y + bounds.Height * (.5 - (frame[i] * .65 + frame[WavePoints + i] * .35) * .47));
                    if (i == 0) path.BeginFigure(point, false, false); else path.LineTo(point, true, false);
                }
            }
            geometry.Freeze(); dc.DrawGeometry(null, _ambientPens[layer], geometry);
        }
        dc.Pop();
    }
    private static SolidColorBrush Brush(string hex) => ThemeService.Brush(hex);
}
