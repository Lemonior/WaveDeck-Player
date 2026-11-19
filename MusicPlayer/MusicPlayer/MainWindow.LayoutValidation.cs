using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayer.Services;
using MusicPlayer.Validation;

namespace MusicPlayer;

public partial class MainWindow
{
    internal async Task ValidateLayoutAsync(Storage storage)
    {
        var report = new List<string>();
        var cursor = DesktopInput.CursorPosition();
        try
        {
            _silentValidation = true; _settings.BackgroundMediaKeys = false; _settings.GlobalPlayPauseShortcut = "Off"; ConfigureGlobalKeys();
            Width = 1180; Height = 930; VolumeSlider.Value = 0;
            foreach (var theme in new[] { "Modern", "OldSchool", "Retro" })
            {
                ThemeService.Apply(theme);
                foreach (var size in new[] { "normal", "minimum", "maximized" })
                {
                    WindowState = WindowState.Normal; Width = size == "minimum" ? MinWidth : 1180; Height = size == "minimum" ? MinHeight : 930;
                    if (size == "maximized") WindowState = WindowState.Maximized;
                    _settings.ChannelAEnabled = _settings.ChannelBEnabled = _settings.ChannelCEnabled = true; ApplyVisualOptions();
                    ChapterTransport.Visibility = Visibility.Visible; await Settle();
                    var allHeight = EqualizerFrame.ActualHeight;
                    var remainingRows = AudioLayout.ActualHeight - AudioLayout.RowDefinitions.Where((_, i) => i is not (2 or 3 or 4 or 6)).Sum(r => r.ActualHeight) - 212;
                    if (remainingRows * .875 / 3 >= 32 && Math.Abs(ChannelARow.ActualHeight - remainingRows / 3 * .875) > 1) throw new Exception($"The 12.5% visual-to-mixer allocation is incorrect in {theme}/{size}: available {remainingRows}, channel {ChannelARow.ActualHeight}, requested {ChannelARow.Height}, grid {AudioLayout.ActualHeight}.");
                    for (var mask = 7; mask >= 0; mask--)
                    {
                        _settings.ChannelAEnabled = (mask & 1) != 0; _settings.ChannelBEnabled = (mask & 2) != 0; _settings.ChannelCEnabled = (mask & 4) != 0; ApplyVisualOptions(); await Settle();
                        foreach (var (channel, row, enabled) in new[] { (ChannelA, ChannelARow, _settings.ChannelAEnabled), (ChannelB, ChannelBRow, _settings.ChannelBEnabled), (ChannelC, ChannelCRow, _settings.ChannelCEnabled) })
                        {
                            if (channel.Visibility != (enabled ? Visibility.Visible : Visibility.Collapsed) || enabled && row.ActualHeight < 32 || !enabled && row.ActualHeight > .1) throw new Exception("A disabled channel kept its layout space.");
                        }
                        if (mask != 7 && EqualizerFrame.ActualHeight <= allHeight) throw new Exception("Hidden visual space did not grow the mixer.");
                        if (ChapterTransport.TranslatePoint(new Point(0, ChapterTransport.ActualHeight), this).Y > ActualHeight - 25) throw new Exception("Collapsing channels clipped the transport controls.");
                        foreach (var tab in Enumerable.Range(0, 3))
                        {
                            SoundTabs.SelectedIndex = tab; UpdateLayout();
                            var corner = SoundTabs.TranslatePoint(new Point(SoundTabs.ActualWidth, SoundTabs.ActualHeight), EqualizerFrame);
                            if (corner.Y > EqualizerFrame.ActualHeight || corner.X > EqualizerFrame.ActualWidth) throw new Exception("The resized mixer clips its tab content.");
                        }
                        SoundTabs.SelectedIndex = 0;
                        if (theme == "OldSchool" && size == "maximized" && mask is 7 or 4 or 0) Capture("layout-" + size + "-channels-" + mask + ".png");
                    }
                }
            }
            report.Add("All eight channel combinations collapse and redistribute space to the mixer across every theme, normal/minimum/maximized size and all sound tabs; transport stays visible.");
            _settings.ChannelAEnabled = _settings.ChannelBEnabled = _settings.ChannelCEnabled = true; ApplyVisualOptions();
            ThemeService.Apply("OldSchool"); WindowState = WindowState.Normal; Width = 1180; Height = 930; Activate(); await Settle();
            WindowState = WindowState.Maximized; await Task.Delay(120); UpdateLayout();
            _headerInputTrace = [];
            var start = HeaderTitle.TranslatePoint(new Point(HeaderTitle.ActualWidth * .5, HeaderTitle.ActualHeight * .5), this);
            var fraction = start.X / ActualWidth; var restoreWidth = RestoreBounds.Width; var restoreHeight = RestoreBounds.Height;
            var screen = PointToScreen(start);
            DesktopInput.MoveMouse(screen); DesktopInput.LeftMouse(true); await Task.Delay(80); DesktopInput.LeftMouse(false); await Task.Delay(100);
            if (WindowState != WindowState.Maximized) throw new Exception("Clicking the header without dragging restored the window.");
            report.Add("An actual Windows click on the maximized header leaves the window maximized.");
            await Task.Delay(600);
            var destination = screen + new Vector(90, 100);
            var drag = Task.Run(async () =>
            {
                DesktopInput.MoveMouse(screen); DesktopInput.LeftMouse(true);
                try { await Task.Delay(80); DesktopInput.MoveMouse(screen + new Vector(35, 45)); await Task.Delay(160); DesktopInput.MoveMouse(destination); await Task.Delay(160); }
                finally { DesktopInput.LeftMouse(false); }
            });
            await drag; await Task.Delay(120); UpdateLayout();
            if (WindowState != WindowState.Normal || Math.Abs(ActualWidth - restoreWidth) > 2 || Math.Abs(ActualHeight - restoreHeight) > 2) throw new Exception($"A real Windows header drag did not restore the remembered window size: state={WindowState}, actual={ActualWidth}x{ActualHeight}, remembered={restoreWidth}x{restoreHeight}, left={Left}, top={Top}, pending={_headerDragStart}, trace={string.Join(" | ", _headerInputTrace)}.");
            var anchor = PointToScreen(new Point(ActualWidth * fraction, start.Y));
            if ((anchor - destination).Length > 5) throw new Exception($"Restoring the window lost its cursor anchor: {anchor} versus {destination}.");
            report.Add("Real Windows mouse-down/move/up on the maximized header restores and moves the window, preserves size and keeps the cursor anchored to the same header position.");
            _headerInputTrace = [];
            Capture("layout-restored-after-drag.png");
            // Native double-click routing must still maximize and restore.
            await Task.Delay(600);
            var target = PointToScreen(HeaderTitle.TranslatePoint(new Point(HeaderTitle.ActualWidth / 2, HeaderTitle.ActualHeight / 2), this));
            await Task.Run(async () =>
            {
                DesktopInput.MoveMouse(target);
                for (var click = 0; click < 2; click++) { DesktopInput.LeftMouse(true); await Task.Delay(35); DesktopInput.LeftMouse(false); await Task.Delay(70); }
            });
            await Task.Delay(100);
            if (WindowState != WindowState.Maximized) throw new Exception($"Header double-click no longer maximizes. Target={target}; foreground={DesktopInput.IsForeground(this)}; trace={string.Join(" | ", _headerInputTrace)}");
            await Task.Delay(600);
            target = PointToScreen(HeaderTitle.TranslatePoint(new Point(HeaderTitle.ActualWidth / 2, HeaderTitle.ActualHeight / 2), this));
            await Task.Run(async () =>
            {
                DesktopInput.MoveMouse(target);
                for (var click = 0; click < 2; click++) { DesktopInput.LeftMouse(true); await Task.Delay(35); DesktopInput.LeftMouse(false); await Task.Delay(70); }
            });
            await Task.Delay(100);
            if (WindowState != WindowState.Normal) throw new Exception("Header double-click no longer restores.");
            report.Add("Real header double-click reliably maximizes and restores after drag-to-restore.");
            File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = true, checks = report }, Storage.JsonOptions));
            async Task Settle() { await Task.Delay(20); UpdateLayout(); }
            void Capture(string name)
            {
                UpdateLayout(); var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(storage.Root, name)); encoder.Save(output);
            }
        }
        catch (Exception ex)
        {
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(Path.Combine(storage.Root, "layout-failure.png"))) encoder.Save(output);
            File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = false, checks = report, error = ex.ToString() }, Storage.JsonOptions));
        }
        finally { DesktopInput.LeftMouse(false); DesktopInput.MoveMouse(cursor); EndHeaderDrag(); }
    }
}
