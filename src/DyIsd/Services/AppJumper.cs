using System;
using System.Diagnostics;
using DyIsd.Native;

namespace DyIsd.Services;

/// <summary>Brings an app's window to the front (Alt + click on the island).</summary>
public static class AppJumper
{
    public static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc" };

    public static bool Focus(params string[] processNames)
    {
        foreach (var name in processNames)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { continue; }

            foreach (var p in procs)
            {
                try
                {
                    var h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) continue;
                    if (Win32.IsIconic(h)) Win32.ShowWindow(h, Win32.SW_RESTORE);
                    Win32.SetForegroundWindow(h);
                    return true;
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        return false;
    }
}
