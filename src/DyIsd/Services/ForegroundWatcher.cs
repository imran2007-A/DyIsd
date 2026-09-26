using System;
using System.Diagnostics;
using System.Windows.Threading;
using DyIsd.Native;

namespace DyIsd.Services;

/// <summary>
/// Knows which app you're in right now. Windows tells us the moment you switch windows,
/// so the island can hide things that belong to the app you're already looking at.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    /// <summary>Process name of the app in front, lowercase, without ".exe" (e.g. "chrome").</summary>
    public string Current { get; private set; } = "";
    public event Action? Changed;

    IntPtr _hook;
    Win32.WinEventProc? _proc; // kept alive so the garbage collector doesn't free it
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };

    public void Start()
    {
        _proc = (_, _, hwnd, _, _, _, _) => Update(hwnd);
        _hook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        Update(Win32.GetForegroundWindow());
        // Backup in case Windows skips a "you switched apps" message.
        _poll.Tick += (_, _) => Update(Win32.GetForegroundWindow());
        _poll.Start();
    }

    void Update(IntPtr hwnd)
    {
        string name = NameOf(hwnd);
        if (name.Length == 0 || name == "dyisd" || name == Current) return; // ignore our own windows
        Current = name;
        Log.Write("in front: " + name);
        Changed?.Invoke();
    }

    static string NameOf(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return "";
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    public bool IsAny(params string[] names)
    {
        foreach (var n in names)
            if (n == Current) return true;
        return false;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Win32.UnhookWinEvent(_hook);
    }
}
