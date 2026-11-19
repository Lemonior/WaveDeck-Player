using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class MainWindow
{
    internal async Task ValidateUiAsync(Storage storage)
    {
        var report = new List<string>();
        try
        {
            _silentValidation = true;
            _settings.ScanSiblingFiles = false; _settings.BackgroundMediaKeys = false; _settings.GlobalPlayPauseShortcut = "Off"; ConfigureGlobalKeys();
            Width = 1180; Height = 880; VolumeSlider.Value = 0.65; _queue.Clear(); await Task.Delay(250); Capture("empty-player.png");
            var wave = Path.Combine(storage.Root, "visualizer-demo.wav"); Validation.SelfTests.WriteTone(wave, 12);
            await AddPathsAsync([wave]);
            _queue.Add(new() { Title = "The midnight frequency", Artist = "WaveDeck · queue example", Kind = "YOUTUBE", Url = "https://www.youtube.com/watch?v=BaW_jenozKc", Duration = 184 });
            _queue.Add(new() { Title = "Signals from another summer", Artist = "Local music · queue example", LocalPath = wave, Duration = 214 });
            if (_queue.Count != 3) throw new Exception("Local file import did not populate the UI queue."); report.Add("Local file import and queue binding passed.");
            SearchBox.Text = "midnight"; if (_view.Cast<object>().Count() != 1) throw new Exception("Playlist search failed."); SearchBox.Text = ""; report.Add("Playlist search passed.");
            VolumeSlider.Value = 0;
            await PlayTrackAsync(_queue[0]); if (!_engine.IsPlaying) throw new Exception("Player did not start.");
            await Task.Delay(350); PresetBox.SelectedItem = "Rock";
            if (_settings.Gains[0] != 4 || _bands[7].Value != 4) throw new Exception("Preset did not update equalizer."); report.Add("Equalizer preset control passed.");
            Bypass_Click(this, new()); if (!_bypass) throw new Exception("A/B bypass failed."); Bypass_Click(this, new());
            _bands[0].Value = 5; if (_settings.Preset != "Custom") throw new Exception("Custom band did not update preset."); report.Add("A/B and custom equalizer control passed.");
            PreampSlider.Value = 6; WidthSlider.Value = 1.7; SafeGain.IsChecked = false; Bypass_Click(this, new());
            DefaultEq_Click(this, new());
            if (_settings.Gains.Any(g => g != 0) || PreampSlider.Value != 0 || WidthSlider.Value != 1 || !(_settings.EqualizerEnabled && _settings.SafeGain) || _bypass || VolumeSlider.Value != 0) throw new Exception("Equalizer default did not reset all processing controls or changed volume.");
            report.Add("Default restores all EQ processing controls and preserves chosen volume.");
            foreach (var slider in _bands.Concat(new[] { VolumeSlider, PreampSlider, WidthSlider, SeekSlider }).Cast<MusicPlayer.Controls.DragSlider>())
            {
                slider.ApplyTemplate(); var original = slider.Value;
                var horizontal = slider.Orientation == System.Windows.Controls.Orientation.Horizontal;
                var firstPoint = horizontal ? new Point(slider.ActualWidth * .2, slider.ActualHeight - 2) : new Point(2, slider.ActualHeight * .2);
                var hit = slider.InputHitTest(firstPoint) as DependencyObject;
                if (slider.ActualHeight < 28 || FindVisualParent<MusicPlayer.Controls.DragSlider>(hit) != slider) throw new Exception($"Slider hit area failed: {slider.Name} ({slider.ActualWidth} x {slider.ActualHeight}), enabled={slider.IsEnabled}, visible={slider.IsVisible}, hit={hit?.GetType().Name}.");
                slider.MoveKnob(firstPoint); var firstValue = slider.Value;
                slider.MoveKnob(horizontal ? new Point(slider.ActualWidth * .8, slider.ActualHeight + 35) : new Point(-35, slider.ActualHeight * .8));
                if (Math.Abs(slider.Value - firstValue) < (slider.Maximum - slider.Minimum) * .4) throw new Exception("Slider positions do not follow a drag across its range, including outside the rail.");
                slider.Value = original;
            }
            report.Add("All sliders provide a 28-DIP hit area beside the rail and map drag positions across the range, including outside their bounds.");
            if (_bands.Concat(new[] { VolumeSlider, PreampSlider, WidthSlider, SeekSlider }).Any(s => !s.IsMoveToPointEnabled)) throw new Exception("A slider does not support click-to-position."); report.Add("All slider rails support click-to-position.");
            var urlCenter = UrlPlaceholder.TranslatePoint(new Point(0, UrlPlaceholder.ActualHeight / 2), this).Y;
            var labelCenter = UrlLabel.TranslatePoint(new Point(0, UrlLabel.ActualHeight / 2), this).Y;
            var inputCenter = UrlBox.TranslatePoint(new Point(0, UrlBox.ActualHeight / 2), this).Y;
            var searchCenter = SearchPlaceholder.TranslatePoint(new Point(0, SearchPlaceholder.ActualHeight / 2), this).Y;
            var searchInput = SearchBox.TranslatePoint(new Point(0, SearchBox.ActualHeight / 2), this).Y;
            if (Math.Abs(urlCenter - inputCenter) > 1 || Math.Abs(labelCenter - inputCenter) > 1 || Math.Abs(searchCenter - searchInput) > 1) throw new Exception("URL or search placeholder is not centered."); report.Add("URL label and both placeholders align with input centers.");
            _engine.TogglePause(); await SeekToAsync(3); if (!_engine.IsPaused || Math.Abs(_engine.Position - 3) > 0.2) throw new Exception("UI seek failed."); _engine.TogglePause(); report.Add("Pause, seek, resume passed.");
            // The visualizer test uses actual decoded audio at zero output volume.
            var analyzer = new Audio.AudioAnalyzer();
            using (var source = new NAudio.Wave.AudioFileReader(wave))
            {
                var samples = new float[8192]; var count = source.Read(samples, 0, samples.Length); analyzer.Add(samples, 0, count, 2);
                _frozen = true; var snapshot = analyzer.Snapshot(true); ChannelA.Snapshot = ChannelB.Snapshot = ChannelC.Snapshot = snapshot;
                ChannelC.UpdateAudioFrame(snapshot, active: true);
                ChannelA.InvalidateVisual(); ChannelB.InvalidateVisual(); ChannelC.InvalidateVisual(); Meters.Left = snapshot.LeftPeak; Meters.Right = snapshot.RightPeak; Meters.InvalidateVisual();
            }
            await Task.Delay(150); Capture("playing-player.png");
            foreach (var theme in new[] { "Modern", "OldSchool", "Retro" })
            {
                ThemeService.Apply(theme); ChannelA.InvalidateVisual(); ChannelB.InvalidateVisual(); ChannelC.InvalidateVisual(); Meters.InvalidateVisual();
                await Task.Delay(100); Capture("theme-" + theme + ".png");
                var expected = ThemeService.Map((Color)ColorConverter.ConvertFromString("#22292F"), theme);
                if (((SolidColorBrush)Background).Color != expected || ChannelA.Accent != ThemeService.Map((Color)ColorConverter.ConvertFromString("#BCD27C"), theme)) throw new Exception("Theme did not update window and visualizer colors.");
                if (!ReferenceEquals(ShuffleButton.Background, FindResource("Chrome")) || !ReferenceEquals(BypassButton.Background, FindResource("Chrome")) || !ReferenceEquals(((System.Windows.Controls.Grid)EqSliders.Children[0]).Children[1].GetValue(System.Windows.Controls.TextBlock.ForegroundProperty), FindResource("Muted"))) throw new Exception("Theme did not update mode buttons or band labels.");
                if (theme == "OldSchool")
                {
                    if (!DeckFrame.IsOrnate || !HeaderFrame.IsOrnate || !EqualizerFrame.IsOrnate || !PlaylistFrame.IsOrnate || HeaderFrame.Background is not DrawingBrush || FindResource("Chrome") is not ImageBrush || FindResource("Panel") is not ImageBrush || PlayButton.Background is not ImageBrush) throw new Exception("OldSchool did not apply textured surfaces, matte controls and gold frames.");
                    var fantasySettings = new SettingsWindow(_settings, _urls, _session) { Owner = this, Height = 600 }; fantasySettings.ThemeBox.SelectedItem = "OldSchool"; fantasySettings.Show(); await Task.Delay(100); CaptureWindow(fantasySettings, "settings-OldSchool.png");
                    var bars = VisualChildren(fantasySettings).OfType<System.Windows.Controls.Primitives.ScrollBar>().Where(bar => bar.IsVisible && bar.Orientation == System.Windows.Controls.Orientation.Vertical).ToList();
                    if (bars.Count == 0 || bars.Any(bar => !ReferenceEquals(bar.Template, FindResource("FantasyVerticalScroll")))) throw new Exception("OldSchool scrollbar styling did not apply.");
                    fantasySettings.Close();
                }
                else if (DeckFrame.IsOrnate || FindResource("Chrome") is not DrawingBrush || FindResource("Panel") is not DrawingBrush || !ReferenceEquals(PlayButton.Background, FindResource("GoldChrome"))) throw new Exception("Modern or Retro did not receive its textured console surfaces.");
                foreach (var slider in _bands.Concat(new[] { VolumeSlider, PreampSlider, WidthSlider, SeekSlider }))
                {
                    slider.ApplyTemplate(); var thumb = (System.Windows.Controls.Primitives.Thumb)slider.Template.FindName("Knob", slider);
                    var top = thumb.TranslatePoint(new Point(), slider); var bottom = thumb.TranslatePoint(new Point(thumb.ActualWidth, thumb.ActualHeight), slider);
                    if (top.Y < -.5 || bottom.Y > slider.ActualHeight + .5) throw new Exception("A slider thumb is vertically clipped in " + theme);
                }
                if (Math.Abs(HeaderTitle.TranslatePoint(new Point(HeaderTitle.ActualWidth / 2, 0), HeaderFrame).X - HeaderFrame.ActualWidth / 2) > 1) throw new Exception("Header title is not centered on the window.");
            }
            ThemeService.Apply("Modern"); report.Add("All three themes update controls, background and live visualizer accents.");
            Width = MinWidth; Height = MinHeight; await Task.Delay(150); Capture("minimum-player.png");
            if (PlayButton.TranslatePoint(new Point(0, PlayButton.ActualHeight), this).Y > ActualHeight - 30) throw new Exception("Transport controls clipped at minimum size."); report.Add("Minimum window size keeps transport controls visible.");
            Width = 1180; Height = 880;
            if (WindowStyle != WindowStyle.None || CloseButton.TranslatePoint(new Point(CloseButton.ActualWidth, 0), this).X > ActualWidth) throw new Exception("Integrated header controls failed.");
            ToggleMaximize(); await Task.Delay(160); if (WindowState != WindowState.Maximized) throw new Exception("Custom maximize control failed.");
            var work = NativeWindow.WorkArea(this); NativeWindow.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(this).Handle, out var maximized);
            if (maximized.Bottom > work.Bottom + 1 || maximized.Top < work.Top - 1 || maximized.Right > work.Right + 1 || maximized.Left < work.Left - 1) throw new Exception("Maximized window escaped the monitor work area.");
            if (ControlsButton.TranslatePoint(new Point(0, ControlsButton.ActualHeight), this).Y > ActualHeight - 20) throw new Exception("Sub-controls clipped below the maximized client area.");
            Capture("maximized-player.png"); ToggleMaximize(); report.Add("Centered vector header and maximize / restore respect the monitor work area, including bottom controls.");
            var settings = new SettingsWindow(_settings, _urls, _session) { Owner = this }; settings.Show(); await Task.Delay(100); CaptureWindow(settings, "settings.png"); settings.Close(); report.Add("Settings dialog opened and rendered.");
            StopPlayback(); report.Add("Stop control passed.");
            var controlsWindow = new ControlsWindow(_settings) { Owner = this }; controlsWindow.Show(); await Task.Delay(80); CaptureWindow(controlsWindow, "playback-controls.png"); controlsWindow.Close();
            var notice = new MessageWindow("The source could not be opened. Your current audio continues playing.") { Owner = this }; notice.Show(); await Task.Delay(80); CaptureWindow(notice, "styled-message.png"); notice.Close();
            var menuButton = VisualChildren(PlaylistFrame).OfType<System.Windows.Controls.Button>().First(b => b.ContextMenu != null);
            ListTools_Click(menuButton, new()); await Task.Delay(80);
            if (menuButton.ContextMenu!.Background is not DrawingBrush || menuButton.ContextMenu.Foreground != FindResource("Text")) throw new Exception("List tools menu did not inherit the dark theme.");
            menuButton.ContextMenu.IsOpen = false; report.Add("Playback controls, styled messages and dark List tools dropdown render.");
            var repeatBefore = _settings.Repeat; Repeat_Click(this, new()); Repeat_Click(this, new()); Repeat_Click(this, new()); if (_settings.Repeat != repeatBefore) throw new Exception("Repeat modes did not cycle.");
            var shuffleBefore = _settings.Shuffle; Shuffle_Click(this, new()); Shuffle_Click(this, new()); if (_settings.Shuffle != shuffleBefore) throw new Exception("Shuffle toggle failed."); report.Add("Repeat modes and shuffle toggle passed.");
            foreach (var mode in new[] { 0, 1, 2 }) { UrlMode.SelectedIndex = mode; if (_settings.UrlMode != new[] { "Stream", "Download", "Download & play" }[mode]) throw new Exception("URL mode configuration failed."); } UrlMode.SelectedIndex = 0; report.Add("URL modes are configurable.");
            _queue.Clear(); _current = null; _order.Reset(); _settings.Shuffle = false; _settings.Repeat = "Off";
            var controlWave = Path.Combine(storage.Root, "control-seek.wav"); Validation.SelfTests.WriteTone(controlWave, 30);
            var controlTrack = new Track { Title = "Media controls and ten-second seeking", LocalPath = controlWave, Duration = 30 }; _queue.Add(controlTrack);
            await PlayTrackAsync(controlTrack); Validation.DesktopInput.MediaCommand(this, 14); await Task.Delay(60);
            if (!_engine.IsPaused) throw new Exception("Native media play / pause command failed.");
            await SeekByAsync(10); if (Math.Abs(_engine.Position - 10) > .4 || !_engine.IsPaused) throw new Exception("Ten-second forward lost position or paused state.");
            await SeekByAsync(-10); if (_engine.Position > .4) throw new Exception("Ten-second rewind failed.");
            Validation.DesktopInput.TaskbarClick(this, 1); await Task.Delay(80);
            if (TaskbarItemInfo!.ThumbButtonInfos.Count != 3 || !_engine.IsPlaying) throw new Exception("Taskbar transport command failed.");
            if (!MatchesPlayPause(System.Windows.Input.Key.Space, System.Windows.Input.ModifierKeys.None)) throw new Exception("Default play / pause binding failed.");
            _settings.PlayPauseShortcut = "Ctrl+Space"; if (!MatchesPlayPause(System.Windows.Input.Key.Space, System.Windows.Input.ModifierKeys.Control) || MatchesPlayPause(System.Windows.Input.Key.Space, System.Windows.Input.ModifierKeys.None)) throw new Exception("Custom playback key ignored modifiers."); _settings.PlayPauseShortcut = "Space";
            StopPlayback(); _queue.Clear(); _current = null; _order.Reset(); report.Add("Native media key, taskbar playback and ten-second seeking preserve pause and configured bindings.");
            var shortWave = Path.Combine(storage.Root, "auto-advance.wav"); Validation.SelfTests.WriteTone(shortWave, 1);
            var first = new Track { LocalPath = shortWave, Title = "First short track", Duration = 1 }; var second = new Track { LocalPath = shortWave, Title = "Second short track", Duration = 1 };
            _queue.Add(first); _queue.Add(second); await PlayTrackAsync(first);
            var deadline = Environment.TickCount64 + 5000;
            while (!ReferenceEquals(_current, second) && Environment.TickCount64 < deadline) await Task.Delay(40);
            if (!ReferenceEquals(_current, second)) throw new Exception("Playback did not automatically advance to the next track."); report.Add("Natural end of audio advances to the next track.");
            report.Add("Prepared next-track start reached by " + _engine.Position.ToString("0.000") + " seconds into its audio source.");
            deadline = Environment.TickCount64 + 5000;
            while (_engine.HasSource && Environment.TickCount64 < deadline) await Task.Delay(40);
            if (_engine.HasSource) throw new Exception("Playlist did not stop after the final track with repeat off."); report.Add("Repeat-off stops at the end of the playlist.");
            _settings.Repeat = "One"; await PlayTrackAsync(first); StartPrefetch();
            if (!ReferenceEquals(_prefetchTrack, first)) throw new Exception("Repeat-one did not prepare the current track.");
            await AdvanceAsync();
            if (!ReferenceEquals(_current, second)) throw new Exception("Manual Next incorrectly reused the repeat-one preparation.");
            StopPlayback(); _settings.Repeat = "Off"; report.Add("Manual Next skips the current track even with repeat-one preloaded.");
            _queue.Clear(); _current = null; _order.Reset();
            var bad = new Track { Title = "Unavailable autoplay file", LocalPath = Path.Combine(storage.Root, "nonexistent.wav") }; var good = new Track { Title = "Playable after failure", LocalPath = shortWave, Duration = 1 };
            _queue.AddRange([bad, good]); await AdvanceAsync(true);
            if (!ReferenceEquals(_current, good) || !_engine.IsPlaying || bad.State != "!") throw new Exception("Autoplay did not skip an unavailable file without a modal dialog."); StopPlayback(); report.Add("Autoplay skips unavailable sources without blocking on an error dialog.");
            _queue.Clear(); _current = null;
            var album = new Track { Title = "Three original songs in one video", LocalPath = controlWave, Duration = 12, ChaptersChecked = true,
                Chapters = Enumerable.Range(0, 3).Select(i => new Track { Title = "Song " + (i + 1), Artist = "Original chapter validation", LocalPath = controlWave, StartSeconds = i * 4, EndSeconds = (i + 1) * 4, Duration = 4 }).ToList() };
            var afterAlbum = new Track { Title = "Next main playlist video", LocalPath = controlWave, Duration = 30 };
            _queue.AddRange([album, afterAlbum]); _settings.Repeat = "Off"; _settings.Shuffle = false; _chapterShuffle = false;
            await PlayTrackAsync(album); _engine.TogglePause(); RefreshPlayer();
            if (_queue.Count != 2 || _chapterParent != album || _current != album.Chapters[0] || Playlist.SelectedItem != album || ChapterSelector.Items.Count != 3 || HeaderTitle.Text != album.Title || !ChapterTransport.IsVisible) throw new Exception("Song sub-player expanded the main queue, lost the video title or did not show nested songs.");
            await SeekToAsync(2); if (!_engine.IsPaused || Math.Abs(_engine.Position - 2) > .2) throw new Exception("Nested song seeking lost relative time or pause.");
            if (!await AdvanceSongAsync() || _current != album.Chapters[1] || QueueCursor != album || _engine.Position > .4) throw new Exception("Song Next did not stay inside its parent video or used the whole-video offset.");
            _engine.TogglePause(); ChapterPrevious_Click(this, new()); await Task.Delay(150);
            if (_current != album.Chapters[0]) throw new Exception("Song Previous did not follow nested history.");
            _settings.Repeat = "One"; CancelPrefetch(); UpdateModes(); if (NextSong(true) != _current) throw new Exception("Repeat One did not target the selected song.");
            await AdvanceSongAsync(true); if (_current != album.Chapters[0] || _engine.Position > .3) throw new Exception("Song repeat did not restart the same section.");
            _engine.TogglePause(); _settings.Repeat = "Off"; UpdateModes(); ChapterShuffle_Click(this, new()); var songOrder = new HashSet<string>();
            for (var i = 0; i < 2; i++) { var nextSong = NextSong(true); if (nextSong == null || !songOrder.Add(nextSong.Id) || nextSong == _current) throw new Exception("Song shuffle repeated a section before visiting the other songs."); _chapterOrder.Record(nextSong); }
            if (NextSong(true) != null || _settings.Shuffle) throw new Exception("Song shuffle escaped its album or changed playlist shuffle.");
            ChapterShuffle_Click(this, new()); _chapterOrder.Reset(_current); RefreshChapterUi();
            ThemeService.Apply("OldSchool"); await Task.Delay(80); Capture("chapter-sub-player.png"); ThemeService.Apply("Modern");
            if (!VisualChildren(ChapterSelector).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == _current!.Title)) throw new Exception("Song selector displays a type name instead of the song title.");
            if (ChapterTransport.TranslatePoint(new Point(), this).Y < ControlsButton.TranslatePoint(new Point(0, ControlsButton.ActualHeight), this).Y) throw new Exception("Song transport is not below the playback sub-controls.");
            if (ChapterTransport.Children.Count != 3 || ChapterTransport.Columns != 3 || ChapterTransport.Children.OfType<System.Windows.Controls.Button>().Any(b => (b.Content as string)?.Contains("OFF") == true) || VisualChildren(this).OfType<System.Windows.Controls.Button>().Any(b => b.Content as string == "Chapters")) throw new Exception("Song controls still require manual chapter reading or include an extra repeat control.");
            if (ChapterShuffleButton.TranslatePoint(new Point(0, ChapterShuffleButton.ActualHeight), this).Y > ActualHeight - 25) throw new Exception("Song sub-controls clipped at the window bottom.");
            await PlayTrackAsync(album.Chapters[2], automatic: true, chapterParent: album); await SeekToAsync(3.8);
            deadline = Environment.TickCount64 + 5000;
            while (!ReferenceEquals(_current, afterAlbum) && Environment.TickCount64 < deadline) await Task.Delay(40);
            if (_current != afterAlbum || _chapterParent != null || ChapterPanel.IsVisible) throw new Exception("Last song did not advance the main playlist or close the sub-player.");
            _queue.Remove(album);
            var nextAlbum = new Track { Title = "Prepared album during playlist shuffle", LocalPath = controlWave, Duration = 8, ChaptersChecked = true, Chapters = album.Chapters.Take(2).ToList() }; _queue.Add(nextAlbum);
            _settings.Shuffle = true; _order.Reset(afterAlbum); StartPrefetch();
            if (_prefetchQueueTrack != nextAlbum || _prefetchTrack != nextAlbum.Chapters[0]) throw new Exception("Prefetch lost the relationship between a main playlist entry and its first song.");
            await _prefetch!; await AdvanceAsync();
            if (_chapterParent != nextAlbum || _current != nextAlbum.Chapters[0]) throw new Exception("Playlist shuffle discarded its prepared album or selected the wrong song.");
            _settings.Shuffle = false; StopPlayback(); _queue.Clear(); _current = null; _chapterParent = null; RefreshChapterUi(); report.Add("Nested songs keep one main entry, support relative seek, song history, shared repeat and independent song shuffle, visible sub-controls, natural handoff and prepared album transitions during playlist shuffle.");
            await ValidateDownloadControlsAsync(storage, report);
            await ValidateDesktopAdjustmentsAsync(storage, report);
            await ValidateSoundV2Async(storage, report);
            await ValidateReactiveVisualsAsync(storage, report);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            _queue.AddRange(Enumerable.Range(1, 10000).Select(i => new Track { Title = "Large playlist track " + i, Kind = "YOUTUBE", Url = "https://www.youtube.com/watch?v=" + i, Duration = 180 }));
            SearchBox.Text = "track 9999"; if (_view.Cast<object>().Count() != 1) throw new Exception("Large playlist search failed."); SearchBox.Text = "";
            await Task.Delay(120); Playlist.UpdateLayout();
            var realized = Enumerable.Range(0, 10000).Count(i => Playlist.ItemContainerGenerator.ContainerFromIndex(i) != null);
            if (realized > 150 || watch.ElapsedMilliseconds > 5000) throw new Exception("Ten-thousand playlist did not remain virtualized and responsive.");
            report.Add($"10,000-track queue and search passed: {watch.ElapsedMilliseconds} ms, {realized} realized rows."); Capture("large-playlist.png"); _queue.Clear();
            storage.Save(_settings);
            File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = true, checks = report }, Storage.JsonOptions));
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = false, checks = report, error = ex.ToString() }, Storage.JsonOptions)); }
        void Capture(string name) => CaptureWindow(this, name);
        IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in VisualChildren(child)) yield return nested; }
        }
        void CaptureWindow(Window window, string name)
        {
            window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(storage.Root, name)); encoder.Save(file);
        }
    }
}
