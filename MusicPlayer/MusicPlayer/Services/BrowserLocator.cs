using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MusicPlayer.Services;

public sealed record BrowserChoice(string Executable, string Name, bool IsDefault);

public static class BrowserLocator
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "AssocQueryStringW")]
    private static extern int AssocQueryString(uint flags, uint kind, string association, string? verb, StringBuilder? output, ref uint length);

    public static string? DefaultExecutable()
    {
        var registered = RegisteredDefault();
        if (registered != null) return registered;
        // Ask Windows for the effective HTTPS association; never inspect browser profiles.
        uint length = 0; AssocQueryString(0x1000, 2, "https", "open", null, ref length);
        if (length is > 0 and < 32768)
        {
            var output = new StringBuilder((int)length);
            if (AssocQueryString(0x1000, 2, "https", "open", output, ref length) == 0 && File.Exists(output.ToString())) return output.ToString();
        }
        return null;
    }
    private static string? RegisteredDefault()
    {
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            if (choice?.GetValue("ProgId") is not string id) return null;
            using var command = Registry.ClassesRoot.OpenSubKey(id + @"\shell\open\command");
            var path = ParseExecutable(command?.GetValue("") as string);
            return path != null && File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }
    public static string? ParseExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        var end = command.StartsWith('"') ? command.IndexOf('"', 1) : command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) is var i && i >= 0 ? i + 4 : -1;
        if (end <= 0) return null;
        var path = command.StartsWith('"') ? command[1..end] : command[..end];
        return Path.IsPathFullyQualified(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }
    public static BrowserChoice? Describe(string? executable, bool isDefault)
    {
        if (executable == null) return null;
        var name = Path.GetFileName(executable).ToLowerInvariant() switch
        {
            "brave.exe" => "Brave", "chrome.exe" => "Google Chrome", "msedge.exe" => "Microsoft Edge",
            "vivaldi.exe" => "Vivaldi", "chromium.exe" => "Chromium", "opera.exe" => "Opera", _ => null
        };
        return name == null ? null : new(executable, name, isDefault);
    }
    public static BrowserChoice? Prefer(string? defaultExecutable, IEnumerable<string> installed)
    {
        var preferred = Describe(defaultExecutable, true);
        if (preferred != null && File.Exists(preferred.Executable)) return preferred;
        return installed.Where(File.Exists).Select(path => Describe(path, false)).FirstOrDefault(browser => browser != null);
    }
    public static BrowserChoice? FindPreferred()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFiles86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Prefer(DefaultExecutable(),
        [
            Path.Combine(programFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(localData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(localData, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(localData, "Vivaldi", "Application", "vivaldi.exe")
        ]);
    }
}
