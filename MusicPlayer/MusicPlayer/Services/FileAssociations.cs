using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MusicPlayer.Services;

public static class FileAssociations
{
    public static readonly string[] Extensions = [".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".aiff", ".aif", ".mp4", ".webm", ".m3u", ".m3u8", ".wavedeck"];
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint first, nint second);
    internal static string OpenCommand(string executable) => $"\"{Path.GetFullPath(executable)}\" \"%1\"";
    public static void Register(string executable, RegistryKey? root = null)
    {
        root ??= Registry.CurrentUser;
        var command = OpenCommand(executable);
        void Value(string path, string name, object value, RegistryValueKind kind = RegistryValueKind.String)
        { using var key = root.CreateSubKey(path); key.SetValue(name, value, kind); }
        var classes = @"Software\Classes\";
        foreach (var id in new[] { "WaveDeck.Audio", "WaveDeck.Playlist" })
        {
            Value(classes + id, "", id.EndsWith("Audio") ? "WaveDeck audio" : "WaveDeck playlist");
            Value(classes + id, "FriendlyTypeName", id.EndsWith("Audio") ? "WaveDeck audio" : "WaveDeck playlist");
            Value(classes + id + @"\DefaultIcon", "", $"\"{Path.GetFullPath(executable)}\",0");
            Value(classes + id + @"\shell\open", "MultiSelectModel", "Player");
            Value(classes + id + @"\shell\open\command", "", command);
        }
        foreach (var extension in Extensions)
        {
            var id = extension is ".m3u" or ".m3u8" or ".wavedeck" ? "WaveDeck.Playlist" : "WaveDeck.Audio";
            Value(classes + extension + @"\OpenWithProgids", id, Array.Empty<byte>(), RegistryValueKind.None);
            Value(@"Software\WaveDeck\Capabilities\FileAssociations", extension, id);
            Value(classes + @"Applications\WaveDeck.exe\SupportedTypes", extension, "");
        }
        Value(classes + @"Applications\WaveDeck.exe", "FriendlyAppName", "WaveDeck");
        Value(classes + @"Applications\WaveDeck.exe\shell\open", "MultiSelectModel", "Player");
        Value(classes + @"Applications\WaveDeck.exe\shell\open\command", "", command);
        Value(@"Software\WaveDeck\Capabilities", "ApplicationName", "WaveDeck");
        Value(@"Software\WaveDeck\Capabilities", "ApplicationDescription", "Music, playlists and live audio visuals.");
        Value(@"Software\WaveDeck\Capabilities", "ApplicationIcon", $"\"{Path.GetFullPath(executable)}\",0");
        Value(@"Software\RegisteredApplications", "WaveDeck", @"Software\WaveDeck\Capabilities");
        Value(classes + @"Directory\shell\WaveDeck.Open", "", "Play folder in WaveDeck");
        Value(classes + @"Directory\shell\WaveDeck.Open", "Icon", executable);
        Value(classes + @"Directory\shell\WaveDeck.Open\command", "", command);
        if (ReferenceEquals(root, Registry.CurrentUser)) SHChangeNotify(0x08000000, 0, 0, 0);
    }
    public static void ChooseDefaults()
    {
        Register(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WaveDeck.exe"));
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=WaveDeck") { UseShellExecute = true });
    }
}
