using System.Net.Http;

namespace MusicPlayer.Services;

public sealed class AppUpdates : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    public AppRelease? Available { get; private set; }
    public async Task<AppRelease?> CheckAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        Available = await new AppReleaseClient(_http).CheckAsync(timeout.Token); return Available;
    }
    public async Task<PreparedUpdate> PrepareAsync(AppRelease release, string dataRoot, IProgress<double>? progress, CancellationToken token)
    {
        var folder = UpdateState.Folder(dataRoot); UpdatePackage.EnsureNoLinks(folder); Directory.CreateDirectory(folder);
        var download = Path.Combine(folder, "download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(download);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(20));
        try
        {
            var archive = await new AppReleaseClient(_http).DownloadAsync(release, download, progress, timeout.Token);
            return await Task.Run(() => UpdatePackage.ExtractAsync(archive, folder, release.Version.ToString(), AppReleaseClient.RuntimeId, timeout.Token), timeout.Token);
        }
        finally { try { Directory.Delete(download, true); } catch { } }
    }
    public void Dispose() => _http.Dispose();
}
