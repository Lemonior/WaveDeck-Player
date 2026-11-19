using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MusicPlayer.Services;

public static partial class NativeWindow
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMax { public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public int Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint window, out Rect rect);
    public static void RespectWorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(handle)?.AddHook((nint hwnd, int message, nint w, nint l, ref bool handled) =>
        {
            if (message != 0x24) return 0;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return 0;
            var bounds = Marshal.PtrToStructure<MinMax>(l);
            bounds.MaxPosition = new(info.Work.Left - info.Monitor.Left, info.Work.Top - info.Monitor.Top);
            bounds.MaxSize = new(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
            Marshal.StructureToPtr(bounds, l, false); handled = true; return 0;
        });
    }
    internal static Rect WorkArea(Window window)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(window).Handle, 2), ref info); return info.Work;
    }
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    public static void ApplyDarkCaption(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var enabled = 1; DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        if (handle == 0) return;
        var bg = ThemeService.Brush("22292F").Color;
        var fg = ThemeService.Brush("DDE3E8").Color;
        var background = bg.R | bg.G << 8 | bg.B << 16; DwmSetWindowAttribute(handle, 35, ref background, sizeof(int));
        var text = fg.R | fg.G << 8 | fg.B << 16; DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
    }
}
