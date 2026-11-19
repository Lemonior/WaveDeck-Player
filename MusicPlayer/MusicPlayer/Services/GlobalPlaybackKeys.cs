using System.Runtime.InteropServices;
using System.Windows.Interop;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public sealed class GlobalPlaybackKeys : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
    private readonly HwndSource _source;
    private readonly Action<int> _command;
    private readonly HashSet<int> _registered = [];
    private const int FirstId = 0x5710;
    public GlobalPlaybackKeys(HwndSource source, Action<int> command)
    { _source = source; _command = command; _source.AddHook(Message); }
    public IReadOnlyList<string> Configure(PlayerSettings settings)
    {
        Clear(); var conflicts = new List<string>();
        void Add(int index, uint modifiers, uint key, string label)
        {
            var id = FirstId + index;
            if (RegisterHotKey(_source.Handle, id, modifiers | 0x4000, key)) _registered.Add(id);
            else conflicts.Add(label);
        }
        if (settings.BackgroundMediaKeys)
        {
            Add(0, 0, 0xB3, "Media play / pause"); Add(1, 0, 0xB0, "Media next");
            Add(2, 0, 0xB1, "Media previous"); Add(3, 0, 0xB2, "Media stop");
        }
        switch (settings.GlobalPlayPauseShortcut)
        {
            case "Ctrl+Alt+Space": Add(4, 3, 0x20, "Ctrl+Alt+Space"); break;
            case "Ctrl+Space": Add(4, 2, 0x20, "Ctrl+Space"); break;
            case "Ctrl+Shift+P": Add(4, 6, 0x50, "Ctrl+Shift+P"); break;
        }
        return conflicts;
    }
    private nint Message(nint hwnd, int message, nint w, nint l, ref bool handled)
    {
        if (message != 0x312 || !_registered.Contains((int)w)) return 0;
        _command((int)w - FirstId); handled = true; return 0;
    }
    private void Clear() { foreach (var id in _registered) UnregisterHotKey(_source.Handle, id); _registered.Clear(); }
    public void Dispose() { Clear(); _source.RemoveHook(Message); }
}
