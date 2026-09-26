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
                if (Win32.SHQueryUserNotificationState(out int state) == 0) fs = state is 2 or 3 or 4;
            }
            catch { }
            if (fs == IsFullscreen) return;
            IsFullscreen = fs;
            Changed?.Invoke();
        };
        _timer.Start();
    }
}
