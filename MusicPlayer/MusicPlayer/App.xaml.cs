using System.Windows;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class App : Application
{
    private SingleInstance? _instance;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pendingFiles = new();
    private bool _deliveringFiles;
    private async void DeliverFiles(MainWindow window, string[] files)
    {
        foreach (var file in files) _pendingFiles.Enqueue(file);
        if (_deliveringFiles) return;
        _deliveringFiles = true;
        try
        {
            do
            {
                await Task.Delay(250);
                var batch = new List<string>(); while (_pendingFiles.TryDequeue(out var file)) batch.Add(file);
                await window.OpenExternalFilesAsync(batch);
            } while (!_pendingFiles.IsEmpty);
        }
        catch (OperationCanceledException) { }
        finally { _deliveringFiles = false; }
    }
    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var rootIndex = Array.IndexOf(e.Args, "--data-dir");
        var root = rootIndex >= 0 && rootIndex + 1 < e.Args.Length ? e.Args[rootIndex + 1] : null;
        if (e.Args.Contains("--keyboard-fixture") && root != null)
        {
            Directory.CreateDirectory(root);
            var fixture = new Window { Title = "WaveDeck independent input fixture", Width = 440, Height = 200, Content = new System.Windows.Controls.TextBox { Text = "Foreground input fixture in a separate process." } };
            MainWindow = fixture; fixture.Show(); fixture.Activate();
            File.WriteAllText(Path.Combine(root, "window.txt"), new System.Windows.Interop.WindowInteropHelper(fixture).Handle.ToString()); return;
        }
        if (e.Args.Contains("--online-test"))
        {
            var result = await Validation.OnlineTests.RunAsync(root ?? Path.Combine(AppContext.BaseDirectory, "online-test"), e.Args.Contains("--only-chapters"));
            Shutdown(result); return;
        }
        if (e.Args.Contains("--self-test"))
        {
            var result = await Validation.SelfTests.RunAsync(root ?? Path.Combine(AppContext.BaseDirectory, "self-test"));
            Shutdown(result); return;
        }
        try
        {
            var storage = new Storage(root);
            var files = e.Args.Where(a => !a.StartsWith("--", StringComparison.Ordinal) && a != root && (File.Exists(a) || Directory.Exists(a))).Select(Path.GetFullPath).ToArray();
            var visualTest = e.Args.Contains("--visual-test");
            var soundTest = e.Args.Contains("--sound-test");
            var layoutTest = e.Args.Contains("--layout-test");
            if (!e.Args.Contains("--smoke-test") && !visualTest && !soundTest && !layoutTest)
            {
                _instance = new(storage.Root);
                if (!_instance.IsPrimary) { await _instance.ForwardAsync(files); Shutdown(); return; }
            }
            DispatcherUnhandledException += (_, args) => { storage.Log(args.Exception); MessageWindow.Show(MainWindow, AudioError.SafeMessage(args.Exception.Message)); args.Handled = true; };
            var window = new MainWindow(storage); MainWindow = window; window.Show();
            if (layoutTest) { await window.ValidateLayoutAsync(storage); Shutdown(); return; }
            if (soundTest) { await window.ValidateSoundAsync(storage); Shutdown(); return; }
            if (visualTest) { await window.ValidateVisualsAsync(storage); Shutdown(); return; }
            if (e.Args.Contains("--smoke-test")) { await Validation.UiSmokeTest.RunAsync(window, storage); Shutdown(); return; }
            _instance?.Listen(paths => Dispatcher.BeginInvoke(() => DeliverFiles(window, paths)));
            if (files.Length > 0) await window.OpenExternalFilesAsync(files);
            else await window.RestoreLastPlaybackAsync();
        }
        catch (Exception ex)
        {
            if ((e.Args.Contains("--smoke-test") || e.Args.Contains("--visual-test") || e.Args.Contains("--sound-test") || e.Args.Contains("--layout-test")) && root != null)
            { Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "ui-results.json"), System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }, Storage.JsonOptions)); }
            else MessageWindow.Show(null, AudioError.SafeMessage(ex.Message), "WaveDeck startup error");
            Shutdown(1);
        }
    }
}
