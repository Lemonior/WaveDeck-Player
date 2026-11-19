using System.Windows;
using System.Windows.Controls;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class MainWindow
{
    private Track? _chapterParent;
    private readonly PlaybackOrder _chapterOrder = new();
    private readonly Stack<Track> _chapterHistory = new();
    private bool _chapterShuffle, _updatingChapterUi;
    private CancellationTokenSource? _chapterReadCancel;
    private Track? QueueCursor => _chapterParent ?? _current;

    private void RefreshChapterUi()
    {
        _updatingChapterUi = true;
        try
        {
            var active = _chapterParent != null;
            ChapterPanel.Visibility = ChapterTransport.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            MinHeight = active ? 900 : 860;
            if (active)
            {
                if (!ReferenceEquals(ChapterSelector.ItemsSource, _chapterParent!.Chapters)) ChapterSelector.ItemsSource = _chapterParent.Chapters;
                ChapterSelector.SelectedItem = _current;
                ChapterAlbum.Text = _chapterParent.Title;
                ChapterPosition.Text = $"SONGS IN THIS VIDEO · {_chapterParent.Chapters.IndexOf(_current!) + 1}/{_chapterParent.Chapters.Count}";
            }
            else { ChapterSelector.ItemsSource = null; }
            ChapterShuffleButton.SetResourceReference(Control.BackgroundProperty, _chapterShuffle ? "GoldChrome" : "Chrome");
            ChapterShuffleButton.ToolTip = "Shuffle songs inside this video: " + (_chapterShuffle ? "on" : "off");
        }
        finally { _updatingChapterUi = false; }
    }

    private void StartChapterDetection(Track parent)
    {
        _chapterReadCancel?.Cancel();
        if (parent.Url.Length == 0 || parent.DirectFallback || UrlService.IsDirectMedia(UrlService.Validate(parent.Url))) return;
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); cancel.CancelAfter(TimeSpan.FromSeconds(45));
        _chapterReadCancel = cancel;
        _ = DetectChaptersAsync(parent, cancel, _navigationVersion);
    }
    private async Task DetectChaptersAsync(Track parent, CancellationTokenSource cancel, int version)
    {
        try
        {
            // Read afresh on every video start, including remembered/downloaded entries.
            await Task.Run(() => _readChapters(parent, cancel.Token), cancel.Token);
            while (_seekCancel != null) await Task.Delay(40, cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            if (_closing || version != _navigationVersion || !ReferenceEquals(QueueCursor, parent) || _playCancel != null || parent.Chapters.Count == 0) return;
            if (_chapterParent != null && parent.Chapters.Contains(_current!)) { RefreshChapterUi(); return; }
            var position = (_chapterParent != null ? _current!.StartSeconds : 0) + _engine.Position;
            var song = parent.Chapters.LastOrDefault(c => c.StartSeconds <= position && position < c.EndSeconds) ?? parent.Chapters[0];
            parent.UseChapters = true; _chapterOrder.Reset(); _chapterHistory.Clear(); CancelPrefetch();
            await PlayTrackAsync(song, false, true, offset: Math.Max(0, position - song.StartSeconds), chapterParent: parent, paused: _engine.IsPaused);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _storage.Log(ex); } // Optional metadata never interrupts playback.
        finally { if (ReferenceEquals(_chapterReadCancel, cancel)) _chapterReadCancel = null; cancel.Dispose(); }
    }

    private Track? NextSong(bool natural, Track? cursor = null)
    {
        if (_chapterParent == null) return null;
        return natural && _settings.Repeat == "One" ? _current : _chapterOrder.Next(_chapterParent.Chapters, cursor ?? _current, _chapterShuffle, "Off", natural);
    }

    private async Task<bool> AdvanceSongAsync(bool natural = false, Track? failed = null)
    {
        if (_chapterParent is not { } parent) return false;
        if (!natural) _navigationVersion++;
        var version = _navigationVersion;
        var cursor = failed ?? _current; var tried = new HashSet<string>(); if (failed != null) tried.Add(failed.Id);
        var cached = _prefetchTrack != null && parent.Chapters.Contains(_prefetchTrack) && (_settings.Repeat != "One" || natural && ReferenceEquals(_prefetchTrack, _current)) ? _prefetchTrack : null;
        while (!_closing)
        {
            var song = cached ?? (failed == null && natural && _settings.Repeat == "One" ? _current : _chapterOrder.Next(parent.Chapters, cursor, _chapterShuffle, "Off", natural));
            cached = null;
            if (song == null || !tried.Add(song.Id)) return false;
            if (await PlayTrackAsync(song, automatic: true, chapterParent: parent)) return true;
            if (version != _navigationVersion) return true;
            cursor = song; failed = song;
        }
        return false;
    }

    private async void ChapterNext_Click(object sender, RoutedEventArgs e)
    { if (!await AdvanceSongAsync()) SetStatus("No playable songs in this video."); }
    private async void ChapterPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_chapterParent is not { } parent || parent.Chapters.Count == 0) return;
        if (_engine.Position > 3) { await SeekToAsync(0); return; }
        var previous = parent.Chapters[(Math.Max(0, parent.Chapters.IndexOf(_current!)) - 1 + parent.Chapters.Count) % parent.Chapters.Count];
        while (_chapterHistory.Count > 0) { var candidate = _chapterHistory.Pop(); if (parent.Chapters.Contains(candidate)) { previous = candidate; break; } }
        if (!await PlayTrackAsync(previous, false, chapterParent: parent)) await AdvanceSongAsync(true, previous);
    }
    private void ChapterShuffle_Click(object sender, RoutedEventArgs e)
    { CancelPrefetch(); _chapterShuffle = !_chapterShuffle; _chapterOrder.Reset(_current); RefreshChapterUi(); }
    private async void ChapterSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingChapterUi || _chapterParent is not { } parent || ChapterSelector.SelectedItem is not Track song || ReferenceEquals(song, _current)) return;
        if (!await PlayTrackAsync(song, chapterParent: parent)) await AdvanceSongAsync(true, song);
    }
    private async void WholeVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_chapterParent is not { } parent) return;
        if (!File.Exists(parent.LocalPath) && parent.Chapters.Any(c => File.Exists(c.LocalPath))) { SetStatus("This download contains separate songs. Use the song selector to play them."); return; }
        var position = (_current?.StartSeconds ?? 0) + _engine.Position; var paused = _engine.IsPaused;
        parent.UseChapters = false;
        if (!await PlayTrackAsync(parent, false, offset: position, paused: paused, wholeVideo: true)) parent.UseChapters = true;
    }
}
