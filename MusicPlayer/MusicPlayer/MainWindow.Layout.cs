using System.Windows;
using System.Windows.Input;

namespace MusicPlayer;

public partial class MainWindow
{
    private Point? _headerDragStart;
    private double _headerDragFraction;
    private bool _headerDragRestored;
    private List<string>? _headerInputTrace;
    private void AudioLayout_Updated(object? sender, EventArgs e) => UpdateAudioLayout();
    private void UpdateAudioLayout()
    {
        if (!_ready || AudioLayout.ActualHeight <= 0) return;
        var fixedHeight = 0d;
        for (var i = 0; i < AudioLayout.RowDefinitions.Count; i++)
            if (i is not (2 or 3 or 4 or 6)) fixedHeight += AudioLayout.RowDefinitions[i].ActualHeight;
        // Keep each visible visual at 87.5% of its former share. The mixer takes
        // that extra space, plus the entire share of any hidden channel.
        var channelHeight = Math.Max(32, (AudioLayout.ActualHeight - fixedHeight - EqualizerRow.MinHeight) * .875 / 3);
        SetRow(ChannelARow, _settings.ChannelAEnabled);
        SetRow(ChannelBRow, _settings.ChannelBEnabled);
        SetRow(ChannelCRow, _settings.ChannelCEnabled);
        void SetRow(System.Windows.Controls.RowDefinition row, bool enabled)
        {
            var height = enabled ? channelHeight : 0;
            row.MinHeight = enabled ? 32 : 0;
            if (!row.Height.IsAbsolute || Math.Abs(row.Height.Value - height) > .1) row.Height = new(height);
        }
    }
    private void BeginHeaderDrag(Point pointer)
    {
        _headerDragStart = pointer;
        _headerDragFraction = Math.Clamp(pointer.X / ActualWidth, 0, 1);
        _headerDragRestored = false;
        HeaderFrame.MouseMove -= Header_MouseMove; HeaderFrame.MouseMove += Header_MouseMove;
        HeaderFrame.MouseLeftButtonUp -= Header_MouseUp; HeaderFrame.MouseLeftButtonUp += Header_MouseUp;
        HeaderFrame.LostMouseCapture -= Header_LostCapture; HeaderFrame.LostMouseCapture += Header_LostCapture;
        HeaderFrame.CaptureMouse();
    }
    private void EndHeaderDrag()
    {
        _headerDragStart = null;
        _headerDragRestored = false;
        HeaderFrame.MouseMove -= Header_MouseMove;
        HeaderFrame.MouseLeftButtonUp -= Header_MouseUp;
        HeaderFrame.LostMouseCapture -= Header_LostCapture;
        if (HeaderFrame.IsMouseCaptured) HeaderFrame.ReleaseMouseCapture();
    }
    private void Header_MouseUp(object sender, MouseButtonEventArgs e) => EndHeaderDrag();
    private void Header_LostCapture(object sender, MouseEventArgs e) => EndHeaderDrag();
    private void Header_MouseMove(object sender, MouseEventArgs e)
    {
        if (_headerDragStart is not { } start) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndHeaderDrag(); return; }
        var pointer = e.GetPosition(this);
        if (!_headerDragRestored && Math.Abs(pointer.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(pointer.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (!_headerDragRestored && WindowState != WindowState.Maximized)
        {
            _headerInputTrace?.Add($"native drag after movement {pointer - start}");
            // Enter the normal native move loop only after a drag starts.
            // Starting it on mouse-down can consume the click sequence and
            // make a double-click unreliable on a captionless window.
            EndHeaderDrag();
            if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
            e.Handled = true; return;
        }
        var screen = PointToScreen(pointer);
        var fraction = _headerDragFraction;
        if (!_headerDragRestored)
        {
            EndHeaderDrag();
            WindowState = WindowState.Normal;
            BeginHeaderDrag(start); _headerDragFraction = fraction; _headerDragRestored = true;
        }
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var screenPosition = transform.Transform(screen);
        Left = screenPosition.X - Width * fraction;
        Top = screenPosition.Y - start.Y;
        // Keep the same capture for the remainder of this gesture. A second
        // native move loop after restoring a captionless window can relocate
        // the cursor/window to the old caption origin instead of this anchor.
        e.Handled = true;
    }
}
