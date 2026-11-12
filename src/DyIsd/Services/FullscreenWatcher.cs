using System;
using System.Windows.Threading;
using DyIsd.Native;

namespace DyIsd.Services;

/// <summary>Asks Windows every 2 seconds whether a full-screen app, game or presentation is running.</summary>
public sealed class FullscreenWatcher
{
    public bool IsFullscreen { get; private set; }
    public event Action? Changed;

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public void Start()
    {
        _timer.Tick += (_, _) =>
        {
            bool fs = false;
            try
            {
                // Windows' own "busy" answer (2) sometimes stays on with nothing full-screen, which
                // hid the island for good; so: a window really covering its screen, or Windows'
                // reliable answers (a DirectX game, presentation mode).
                fs = Monitors.ForegroundIsFullscreen()
                     || (Win32.SHQueryUserNotificationState(out int state) == 0 && state is 3 or 4);
            }
            catch { }
            if (fs == IsFullscreen) return;
            IsFullscreen = fs;
            Changed?.Invoke();
        };
        _timer.Start();
    }
}
