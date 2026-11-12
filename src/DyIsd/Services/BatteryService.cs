using System;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace DyIsd.Services;

public enum BatteryEvent { Charging, Unplugged, Low, Critical }

/// <summary>Checks the battery every 2 seconds and reports plug/unplug and low battery.</summary>
public sealed class BatteryService
{
    /// <summary>Event, percent, and "time left" text when Windows knows it.</summary>
    public event Action<BatteryEvent, int, string?>? Event;

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    WF.PowerLineStatus _lastLine;
    bool _warnedLow, _warnedCritical, _hasBattery;

    public int Percent => (int)Math.Round(WF.SystemInformation.PowerStatus.BatteryLifePercent * 100);

    public void Start()
    {
        var ps = WF.SystemInformation.PowerStatus;
        // 128 = no battery (desktop PC). "Unknown" (255) also has that bit set.
        _hasBattery = (ps.BatteryChargeStatus & WF.BatteryChargeStatus.NoSystemBattery) == 0;
        _lastLine = ps.PowerLineStatus;
        if (!_hasBattery) return;
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    void Poll()
    {
        var ps = WF.SystemInformation.PowerStatus;
        int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
        var line = ps.PowerLineStatus;

        if (line != _lastLine && line != WF.PowerLineStatus.Unknown && _lastLine != WF.PowerLineStatus.Unknown)
        {
            if (line == WF.PowerLineStatus.Online)
            {
                _warnedLow = _warnedCritical = false;
                Event?.Invoke(BatteryEvent.Charging, pct, null);
            }
            else
            {
                Event?.Invoke(BatteryEvent.Unplugged, pct, TimeLeft(ps.BatteryLifeRemaining));
            }
        }
        _lastLine = line;

        if (line == WF.PowerLineStatus.Offline)
        {
            if (pct <= 5 && !_warnedCritical)
            {
                _warnedCritical = _warnedLow = true;
                Event?.Invoke(BatteryEvent.Critical, pct, null);
            }
            else if (pct <= 15 && !_warnedLow)
            {
                _warnedLow = true;
                Event?.Invoke(BatteryEvent.Low, pct, null);
            }
        }
    }

    static string? TimeLeft(int seconds)
    {
        if (seconds <= 0) return null;
        var t = TimeSpan.FromSeconds(seconds);
        return t.Hours > 0 ? $"{t.Hours}h {t.Minutes}m left" : $"{t.Minutes}m left";
    }

    /// <summary>Segoe Fluent battery icon for a percentage.</summary>
    public static string GlyphFor(int pct) => pct >= 95 ? "\uE83F" : ((char)(0xE850 + Math.Clamp(pct / 10, 0, 9))).ToString();
}
