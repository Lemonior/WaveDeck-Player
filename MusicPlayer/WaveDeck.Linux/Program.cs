using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using MusicPlayer.Services;

namespace WaveDeck.Linux;

internal static class Program
{
    public static string[] Arguments = [];
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        var helper = Array.IndexOf(args, "--apply-update"); if (helper >= 0) return helper + 1 < args.Length ? UpdateInstaller.RunHelperAsync(args[helper + 1]).GetAwaiter().GetResult() : 1;
        if (args.Contains("--self-test")) return LinuxTests.RunAsync(DataRoot()).GetAwaiter().GetResult();
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
    public static string? DataRoot() { var index = Array.IndexOf(Arguments, "--data-dir"); return index >= 0 && index + 1 < Arguments.Length ? Arguments[index + 1] : null; }
}
public sealed class App : Application
{
    public override void Initialize() { RequestedThemeVariant = ThemeVariant.Dark; Styles.Add(new FluentTheme()); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var storage = new Storage(Program.DataRoot()); L.Apply(storage.Load().Language);
            var window = new MainWindow(storage); desktop.MainWindow = window;
            if (Program.Arguments.Contains("--smoke-test")) window.Opened += async (_, _) => { var result = await window.ValidateAsync(); desktop.Shutdown(result); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
