using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MusicPlayer.Controls;

/// <summary>Scrub anywhere in the control, with one capture spanning the whole gesture.</summary>
public class DragSlider : Slider
{
    private bool _dragging;
    public event EventHandler? ScrubStarted;
    public event EventHandler? ScrubCompleted;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (!IsEnabled) return;
        if (e.ClickCount > 1) { e.Handled = true; return; }
        Focus();
        if (!CaptureMouse()) return;
        _dragging = true; ScrubStarted?.Invoke(this, EventArgs.Empty);
        MoveKnob(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!_dragging) return;
        if (e.LeftButton != MouseButtonState.Pressed) { Finish(); return; }
        MoveKnob(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!_dragging) return;
        MoveKnob(e.GetPosition(this)); Finish(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    { base.OnLostMouseCapture(e); Finish(); }
    private void Finish()
    {
        if (!_dragging) return;
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        ScrubCompleted?.Invoke(this, EventArgs.Empty);
    }
    internal void MoveKnob(Point point)
    {
        if (Template?.FindName("PART_Track", this) is not Track track) return;
        var local = TranslatePoint(point, track);
        var horizontal = Orientation == Orientation.Horizontal;
        var thumb = horizontal ? track.Thumb.ActualWidth : track.Thumb.ActualHeight;
        var length = (horizontal ? track.ActualWidth : track.ActualHeight) - thumb;
        if (length <= 0) return;
        // Use absolute geometry, so rapid input remains correct before the next layout pass.
        var fraction = ((horizontal ? local.X : local.Y) - thumb / 2) / length;
        if (!horizontal) fraction = 1 - fraction;
        if (IsDirectionReversed) fraction = 1 - fraction;
        if (horizontal && FlowDirection == FlowDirection.RightToLeft) fraction = 1 - fraction;
        var value = Minimum + Math.Clamp(fraction, 0, 1) * (Maximum - Minimum);
        if (IsSnapToTickEnabled && TickFrequency > 0) value = Minimum + Math.Round((value - Minimum) / TickFrequency) * TickFrequency;
        SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
    }
}
