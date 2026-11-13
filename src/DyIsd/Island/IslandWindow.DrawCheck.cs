using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DyIsd.Island;

/// <summary>
/// Self-check: the see-through island window can stop painting when the graphics card resets its
/// drawing (it happened opening WhatsApp): DyIsd keeps running but nothing shows. Every few
/// seconds, if the island should be visible and nothing covers it, the screen pixel just inside
/// its edge must be the island's colour. Twice wrong in a row: hide and re-show the window, which
/// brings the drawing back. If that doesn't help three times in two minutes, exit so the watchdog
/// starts a fresh DyIsd.
/// </summary>
public partial class IslandWindow
{
    readonly DispatcherTimer _drawCheck = new() { Interval = TimeSpan.FromSeconds(4) };
    int _blankChecks;
    readonly List<DateTime> _redraws = new();

    void StartDrawCheck()
    {
        _drawCheck.Tick += (_, _) => { try { CheckDrawn(); } catch (Exception ex) { Log.Error("draw check", ex); } };
        _drawCheck.Start();
    }

    void CheckDrawn()
    {
        if (!_shown || Pill.Opacity < 0.99 || Pill.ActualHeight < 20 || Pill.ActualWidth < 40 ||
            Pill.Background is not SolidColorBrush { Color.A: 255 } bg)
        {
            _blankChecks = 0;
            return;
        }
        var h = new WindowInteropHelper(this).Handle;
        var p = Pill.PointToScreen(new Point(5, Pill.ActualHeight / 2)); // just inside the left edge
        int x = (int)p.X, y = (int)p.Y;
        if (h == IntPtr.Zero || CoveredAt(h, x, y)) { _blankChecks = 0; return; }

        var dc = GetDC(IntPtr.Zero);
        uint px = GetPixel(dc, x, y);
        ReleaseDC(IntPtr.Zero, dc);
        int r = (int)(px & 0xFF), g = (int)((px >> 8) & 0xFF), b = (int)((px >> 16) & 0xFF);
        if (Math.Abs(r - bg.Color.R) + Math.Abs(g - bg.Color.G) + Math.Abs(b - bg.Color.B) < 60) { _blankChecks = 0; return; }
        if (++_blankChecks < 2) return;
        _blankChecks = 0;

        var now = DateTime.Now;
        _redraws.RemoveAll(t => now - t > TimeSpan.FromMinutes(2));
        _redraws.Add(now);
        if (_redraws.Count > 3)
        {
            Log.Write("island: still not drawing after re-showing it 3 times, restarting DyIsd");
            Environment.Exit(3); // the watchdog starts a fresh one
        }
        Log.Write($"island: wasn't drawing (saw #{r:X2}{g:X2}{b:X2} at {x},{y}), re-showing the window");
        ShowWindow(h, 0); // SW_HIDE
        ShowWindow(h, 8); // SW_SHOWNA: back without taking focus
        EnsureTopmost();
    }

    /// <summary>Another visible window above ours covers this point (then the pixel says nothing about us).</summary>
    static bool CoveredAt(IntPtr self, int x, int y)
    {
        for (var w = GetWindow(self, 3 /* GW_HWNDPREV */); w != IntPtr.Zero; w = GetWindow(w, 3))
        {
            if (!IsWindowVisible(w) || !GetWindowRect(w, out var rc)) continue;
            if (DwmGetWindowAttribute(w, 14 /* DWMWA_CLOAKED */, out int cloaked, 4) == 0 && cloaked != 0) continue;
            if (x >= rc.L && x < rc.R && y >= rc.T && y < rc.B) return true;
        }
        return false;
    }

    struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
}
