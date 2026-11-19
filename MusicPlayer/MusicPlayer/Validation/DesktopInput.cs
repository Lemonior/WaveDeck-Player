using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MusicPlayer.Services;

namespace MusicPlayer.Validation;

internal static class DesktopInput
{
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint w, nint l);
    internal static void MediaCommand(Window window, int command) => SendMessage(new WindowInteropHelper(window).Handle, 0x319, 0, (nint)(command << 16));
    internal static void TaskbarClick(Window window, int index) => SendMessage(new WindowInteropHelper(window).Handle, 0x111, (nint)((0x1800 << 16) | index), 0);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPhysicalCursorPos(out NativeWindow.Point position);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetPhysicalCursorPos(int x, int y);
    internal static Point CursorPosition() { GetPhysicalCursorPos(out var position); return new(position.X, position.Y); }
    internal static void MoveMouse(Point point)
    { if (!SetPhysicalCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y))) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    internal static void LeftMouse(bool down)
    {
        var inputs = new[] { new Input { Type = 0, Data = new() { Mouse = new() { Flags = down ? 2u : 4u } } } };
        if (SendInput(1, inputs, Marshal.SizeOf<Input>()) != 1) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    internal static bool IsForeground(Window window) => GetForegroundWindow() == new WindowInteropHelper(window).Handle;
    internal static bool IsForeground(nint window) => GetForegroundWindow() == window;
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AllowSetForegroundWindow(uint process);
    internal static void Foreground(nint window, int process) { AllowSetForegroundWindow((uint)process); ShowWindow(window, 1); SetForegroundWindow(window); }
    internal static void Close(nint window) => SendMessage(window, 0x10, 0, 0);
    internal static void Keys(params ushort[] keys)
    {
        var inputs = keys.Select(k => new Input { Type = 1, Data = new() { Keyboard = new() { Key = k } } })
            .Concat(keys.Reverse().Select(k => new Input { Type = 1, Data = new() { Keyboard = new() { Key = k, Flags = 2 } } })).ToArray();
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
}
