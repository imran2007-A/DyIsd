using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DyIsd.Services;

/// <summary>
/// A normal launch (Windows startup, Start menu) runs this instead of the island: it starts the
/// real DyIsd ("--run") and starts it again whenever it dies, whether that's a crash in the speech
/// engine's native code (which .NET can't catch) or being killed. Only Quit, which exits with 0,
/// stops it for good.
/// </summary>
static class Watchdog
{
    static volatile bool _ending;

    public static async Task RunAsync()
    {
        // Windows is signing out or shutting down: let everything close.
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => _ending = true;
        int quickDeaths = 0;
        while (true)
        {
            var started = DateTime.Now;
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--run") { UseShellExecute = false });
            if (p == null) { Log.Write("watchdog: couldn't start DyIsd"); return; }
            await p.WaitForExitAsync();
            var ran = DateTime.Now - started;
            if (p.ExitCode == 0 || _ending) { Log.Write("watchdog: DyIsd closed normally, stopping"); return; }

            // Crashing right after starting again and again: wait longer each time (1 s … 60 s).
            quickDeaths = ran < TimeSpan.FromMinutes(2) ? quickDeaths + 1 : 0;
            int wait = Math.Min(60, 1 << Math.Min(quickDeaths, 6));
            Log.Write($"watchdog: DyIsd stopped (exit 0x{p.ExitCode:X8}) after {ran:hh\\:mm\\:ss}, starting it again in {wait} s");
            await Task.Delay(wait * 1000);
            if (_ending) return;
        }
    }
}
