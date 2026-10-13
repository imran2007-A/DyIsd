using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace DyIsd.Native;

/// <summary>
/// Which screen a window is on, in real pixels. WPF only knows the main screen
/// (SystemParameters.WorkArea), so the island asks Windows directly to follow you across monitors.
/// </summary>
internal static class Monitors
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    const uint MONITOR_DEFAULTTONEAREST = 2, MONITOR_DEFAULTTOPRIMARY = 1;

    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Win32.POINT pt, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

    public static IntPtr Of(IntPtr hwnd) => MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);

    /// <summary>
    /// The monitor you're working on: the one with the window in front. The desktop and taskbar
    /// belong to no single screen, so for those it's the one the mouse is on.
    /// </summary>
    public static IntPtr Working(IntPtr foreground)
    {
        var sb = new System.Text.StringBuilder(64);
        GetClassName(foreground, sb, sb.Capacity);
        return sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" ? AtCursor() : Of(foreground);
    }
    public static IntPtr Primary => MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY);

    /// <summary>The usable area (without the taskbar) of a monitor, in pixels.</summary>
    public static RECT Work(IntPtr mon)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref info)) GetMonitorInfo(Primary, ref info);
        return info.rcWork;
    }

    /// <summary>1.0 at 100% scaling, 1.25 at 125%…</summary>
    public static double Scale(IntPtr mon)
    {
        try { if (GetDpiForMonitor(mon, 0, out uint x, out _) == 0 && x > 0) return x / 96.0; } catch { }
        return 1.0;
    }

    public static bool IsValid(IntPtr mon)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return mon != IntPtr.Zero && GetMonitorInfo(mon, ref info);
    }

    /// <summary>"left" / "center" / "right" third of the monitor the mouse is on.</summary>
    public static string ZoneAtCursor()
    {
        Win32.GetCursorPos(out var p);
        var w = Work(MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST));
        return p.X < w.Left + w.Width / 3 ? "left" : p.X > w.Left + w.Width * 2 / 3 ? "right" : "center";
    }

    public static IntPtr AtCursor()
    {
        Win32.GetCursorPos(out var p);
        return MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
    }

    /// <summary>Places a window (sized in WPF units) at a pixel position on a monitor, without activating it.</summary>
    public static void Place(Window w, IntPtr hwnd, int x, int y)
    {
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }
}
