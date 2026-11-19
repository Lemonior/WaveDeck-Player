using System.Text.Json;
using System.Windows;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class MainWindow
{
    private long _lastCheckpoint;
    private PlaybackBookmark? _pendingResume;
    private string BookmarkFile => Path.Combine(_storage.Root, "playback.json");
    private void CapturePlayback(bool checkpoint = false)
    {
        if (!_settings.RememberPlayback) { _settings.LastPlayback = null; _pendingResume = null; }
        else if (_current != null && _engine.HasSource)
        {
            _settings.LastPlayback = new()
            {
                Track = QueueCursor!, ChapterId = _chapterParent != null ? _current.Id : null,
                ChapterStart = _chapterParent != null ? _current.StartSeconds : null,
                Position = Math.Min(_engine.Position, Math.Max(0, _current.Duration - .1)), ChapterShuffle = _chapterShuffle
            };
        }
        if (!checkpoint) return;
        try { Storage.WriteAtomic(BookmarkFile, JsonSerializer.Serialize(_settings.LastPlayback, Storage.JsonOptions)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _storage.Log(ex); }
        _lastCheckpoint = Environment.TickCount64;
    }
    public async Task RestoreLastPlaybackAsync(bool paused = true)
    {
        if (!_settings.RememberPlayback) return;
        var saved = _pendingResume ?? _settings.LastPlayback;
        if (_pendingResume == null && File.Exists(BookmarkFile))
        {
            try { saved = JsonSerializer.Deserialize<PlaybackBookmark>(File.ReadAllText(BookmarkFile), Storage.JsonOptions); }
            catch (Exception ex) when (ex is JsonException or IOException) { _storage.Log(ex); }
        }
        if (saved?.Track == null || saved.Track.LocalPath.Length == 0 && saved.Track.Url.Length == 0) return;
        saved.Position = double.IsFinite(saved.Position) ? Math.Max(0, saved.Position) : 0;
        var parent = _queue.FirstOrDefault(t => t.Id == saved.Track.Id) ?? _queue.FirstOrDefault(t =>
            saved.Track.Url.Length > 0 ? t.Url == saved.Track.Url : t.LocalPath.Equals(saved.Track.LocalPath, StringComparison.OrdinalIgnoreCase));
        if (parent == null) { parent = saved.Track; _queue.Add(parent); }
        var chapterIntent = saved.Track.UseChapters;
        if (saved.ChapterId != null && parent.Chapters.Count == 0) parent.Chapters = saved.Track.Chapters;
        var song = saved.ChapterId == null ? null : parent.Chapters.FirstOrDefault(t => t.Id == saved.ChapterId)
            ?? parent.Chapters.FirstOrDefault(t => t.StartSeconds == saved.ChapterStart);
        var absolutePosition = saved.Position + (saved.ChapterStart ?? 0);
        if (song == null && chapterIntent) song = parent.Chapters.LastOrDefault(t => t.StartSeconds <= absolutePosition && absolutePosition < t.EndSeconds);
        var track = song ?? parent;
        var offset = song == null ? absolutePosition : saved.ChapterStart.HasValue ? saved.Position : Math.Max(0, absolutePosition - song.StartSeconds);
        if (track.Duration > 0) offset = Math.Min(offset, Math.Max(0, track.Duration - .1));
        parent.UseChapters = song != null; _chapterShuffle = saved.ChapterShuffle;
        _pendingResume = saved; Playlist.SelectedItem = parent; Playlist.ScrollIntoView(parent);
        TrackTitle.Text = track.Title; HeaderTitle.Text = parent.Title;
        if (await PlayTrackAsync(track, false, automatic: true, offset: offset, chapterParent: song == null ? null : parent, paused: paused, wholeVideo: song == null))
        {
            _pendingResume = null; parent.UseChapters = chapterIntent;
            if (chapterIntent && song == null) StartChapterDetection(parent);
            CapturePlayback(checkpoint: true); SetStatus(paused ? L.T("Ready to resume where you left off. Press Play.") : L.T("Playback resumed where you left off."));
        }
        else { parent.UseChapters = chapterIntent; SetStatus(L.T("Your last song is saved. Press Play to retry, or choose another track.")); }
    }
    public async Task OpenExternalFilesAsync(IEnumerable<string> paths)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath).ToArray();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate();
        if (list.Length == 0) return;
        _pendingResume = null;
        while (_workCancel != null && !_closing) await Task.Delay(50, _lifetime.Token);
        if (_closing) return;
        await AddPathsAsync(list);
        // Explicitly selected files stay ahead of discovered siblings and determine playback.
        var firstFile = list.FirstOrDefault(p => File.Exists(p) && LocalLibrary.Extensions.Contains(Path.GetExtension(p)));
        Track? first = firstFile == null ? null : _queue.FirstOrDefault(t => t.LocalPath.Equals(firstFile, StringComparison.OrdinalIgnoreCase));
        first ??= Directory.Exists(list[0]) ? _queue.FirstOrDefault(t => t.LocalPath.StartsWith(list[0].TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) : null;
        first ??= _queue.FirstOrDefault();
        if (first != null) await PlayRequestedAsync(first);
    }
}
