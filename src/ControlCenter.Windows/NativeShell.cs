using System.Runtime.InteropServices;
using ControlCenter.Core;
namespace ControlCenter.Windows;

public sealed class NativeTray : IDisposable
{
    public const int CallbackMessage = 0x8001;
    public static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private NotifyIconData data;
    private readonly nint icon;
    private bool disposed;
    private readonly TrayRegistration registration;
    public bool IsAvailable => registration.Available;
    public event Action? Toggle;
    public event Action? Menu;
    public NativeTray(nint hwnd, string iconPath, string tooltip = "个人控制中心")
    {
        icon = LoadImage(0, iconPath, 1, 0, 0, 0x10 | 0x40);
        if (icon == 0) throw new IOException("托盘图标无法加载。");
        data = new NotifyIconData { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Hwnd = hwnd, Id = 1,
            Flags = 1 | 2 | 4, Callback = CallbackMessage, Icon = icon, Tip = tooltip, Info = "", InfoTitle = "" };
        registration = new(() => Shell_NotifyIcon(0, ref data),
            () => { data.Version = 4; return Shell_NotifyIcon(4, ref data); },
            () => Shell_NotifyIcon(2, ref data));
        Add();
    }
    public bool Add()
    {
        return registration.Register();
    }
    public void Handle(int message, nint lParam)
    {
        if (TaskbarCreated != 0 && (uint)message == TaskbarCreated) { Add(); return; }
        if (message != CallbackMessage) return;
        var evt = (int)lParam & 0xffff;
        if (registration.Version4 ? evt is 0x400 or 0x401 : evt == 0x202) Toggle?.Invoke();
        if (registration.Version4 ? evt == 0x7b : evt == 0x205) Menu?.Invoke();
    }
    public Rect? GetRect()
    {
        var id = new NotifyIconIdentifier { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Hwnd = data.Hwnd, Id = data.Id };
        return Shell_NotifyIconGetRect(ref id, out var rect) == 0 ? rect : null;
    }
    public void Dispose() { if (disposed) return; disposed = true; registration.Dispose(); if (icon != 0) DestroyIcon(icon); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public nint Hwnd; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyIconIdentifier { public uint Size; public nint Hwnd; public uint Id; public Guid Guid; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier id, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
[StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
[StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
public static class NativeWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    public static (Rect Work, double Scale, Rect? Anchor) Placement(NativeTray? tray, nint window = 0)
    {
        var anchor = tray?.GetRect();
        GetCursorPos(out var point);
        if (anchor is { } r) { point.X = (r.Left + r.Right) / 2; point.Y = (r.Top + r.Bottom) / 2; }
        var monitor = window != 0 ? MonitorFromWindow(window, 2) : MonitorFromPoint(point, 2);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("无法读取屏幕工作区。");
        GetDpiForMonitor(monitor, 0, out var x, out _);
        return (info.Work, x > 0 ? x / 96d : 1, anchor);
    }
    public static void Position(nint hwnd, int x, int y) => SetWindowPos(hwnd, 0, x, y, 0, 0, 0x1 | 0x4 | 0x10);
    public static void Foreground(nint hwnd) => SetForegroundWindow(hwnd);
    public static void Round(nint hwnd) { int value = 2; DwmSetWindowAttribute(hwnd, 33, ref value, 4); }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

