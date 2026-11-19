using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Validation;

public static partial class SelfTests
{
    private static async Task RunChapterRevisionTestsAsync(string root, AudioTools tools, Func<string, Func<Task>, Task> test, Func<Action, Task> sync)
    {
        await test("Chapter refresh preserves saved segments and pending download identities", () => sync(() =>
        {
            var song = new Track { Title = "Old title", StartSeconds = 10, EndSeconds = 20, Duration = 10, DownloadedChapter = true, LocalPath = "C:\\synthetic\\song.mp3" };
            var parent = new Track { Url = "https://example.org/album", Chapters = [song], ChaptersChecked = true };
            UrlService.ApplyChapters("{\"duration\":30,\"chapters\":[{\"title\":\"Updated song title\",\"start_time\":10,\"end_time\":20}]}", parent);
            Assert(parent.Chapters.Count == 1 && ReferenceEquals(parent.Chapters[0], song) && song.Title == "Updated song title" && song.DownloadedChapter && song.LocalPath.EndsWith("song.mp3") && song.MediaStart == 0, "A fresh metadata read detached queued songs or lost saved audio paths.");
            UrlService.ApplyChapters("{\"chapters\":null}", parent); Assert(parent.Chapters.Count == 1 && ReferenceEquals(parent.Chapters[0], song), "Missing remote chapter metadata discarded a saved song.");
        }));
        await test("Nested song playlists retain chapter titles, local files and playback mode", () => sync(() =>
        {
            var parent = new Track { Title = "Original multi-song video", Url = "https://example.org/album", ChaptersChecked = true, UseChapters = true,
                Chapters = [new() { Title = "Song one", StartSeconds = 0, EndSeconds = 10, Duration = 10 }, new() { Title = "Song two", StartSeconds = 10, EndSeconds = 20, Duration = 10, DownloadedChapter = true, LocalPath = "C:\\synthetic\\two.mp3" }] };
            foreach (var extension in new[] { ".wavedeck", ".m3u8" })
            {
                var path = Path.Combine(root, "nested-songs" + extension); Storage.SavePlaylist(path, [parent]); var restored = Storage.LoadPlaylist(path);
                Assert(restored.Count == 1 && restored[0].ChaptersChecked && restored[0].UseChapters && restored[0].Chapters.Count == 2 && restored[0].Chapters[1].Title == "Song two" && restored[0].Chapters[1].DownloadedChapter && restored[0].Chapters[1].MediaStart == 0, "Saving expanded a video into the main queue or lost nested song metadata.");
            }
        }));
        await test("Download-and-play starts the first saved song before remaining segments finish", async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var first = new Track { Title = "First song", Url = "https://example.org/album", StartSeconds = 0, EndSeconds = 10 };
            var second = new Track { Title = "Second song", Url = first.Url, StartSeconds = 10, EndSeconds = 20 };
            var parent = new Track { Title = "Album", Url = first.Url, Chapters = [first, second], ChaptersChecked = true };
            var notices = new System.Collections.Concurrent.ConcurrentQueue<DownloadNotice>();
            var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var settings = new PlayerSettings { DownloadDelaySeconds = 0 }; var urls = new UrlService(tools, settings, dataRoot: root);
            await using var downloads = new DownloadQueue(urls, settings, new ImmediateProgress<DownloadNotice>(notices.Enqueue), async (track, token, _) =>
            {
                if (ReferenceEquals(track, second)) { secondStarted.TrySetResult(); await release.Task.WaitAsync(token); }
                track.DownloadedChapter = true; track.LocalPath = track.Title + ".mp3"; return track.LocalPath;
            }, (track, _) => Task.FromResult<IReadOnlyList<Track>>(track.Chapters));
            downloads.Enqueue([parent], new(true)); await secondStarted.Task.WaitAsync(timeout.Token);
            Assert(notices.Count(n => n.StartPlayback) == 1 && notices.Single(n => n.StartPlayback).Track == first && notices.Single(n => n.StartPlayback).Parent == parent && downloads.Completion(first)!.IsCompletedSuccessfully && !downloads.Completion(parent)!.IsCompleted && !downloads.Completion(second)!.IsCompleted, "Download-and-play waited for the full video or started multiple songs.");
            release.TrySetResult(); var result = await downloads.Completion(parent)!.WaitAsync(timeout.Token);
            Assert(result.Success && parent.LocalPath.Length == 0 && first.DownloadedChapter && second.DownloadedChapter && notices.Count(n => n.StartPlayback) == 1, "Segments overwrote the parent or changed playback after the first saved song.");
        });
    }
    private sealed class ImmediateProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
