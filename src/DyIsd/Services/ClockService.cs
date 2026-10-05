using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using DyIsd.Island;

namespace DyIsd.Services;

/// <summary>
/// Shows the Windows Clock app's running timer or stopwatch on the island.
///
/// Clock doesn't share its timers with other apps, so this reads the numbers off Clock's
/// window every second (UI Automation). That means it only works while Clock is open
/// (minimized is fine) and showing the Timer or Stopwatch page. A number that counts down
/// is a running timer; one that counts up is the stopwatch; one that doesn't move is paused.
/// </summary>
public sealed class ClockService
{
    public ClockState State { get; } = new();
    public event Action? ActiveChanged;
    /// <summary>A Clock timer reached zero.</summary>
    public event Action? TimerDone;

    static readonly string[] Processes = { "Time" }; // the Clock app's process
    static readonly Regex TimeText = new(@"^(?:(\d{1,2}):)?(\d{1,2}):(\d{2})(?:[.,](\d{2}))?$");

    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    Dictionary<int, double> _last = new(); // position on screen -> seconds, from the previous read
    double _lastRunningTimer = -1;
    bool _busy;
    string _loggedTexts = "";

    public void Start()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    void Poll()
    {
        if (_busy) return;
        if (!Uia.Running(Processes))
        {
            if (State.IsActive) Apply(null, null, 0);
            return;
        }
        _busy = true;
        Task.Run(Read).ContinueWith(t =>
        {
            _busy = false;
            if (t.Exception != null) Log.Error("clock", t.Exception.InnerException ?? t.Exception);
        });
    }

    void Read()
    {
        var times = new List<(int Index, double Seconds, bool Centi, string Text)>();
        int i = 0;
        foreach (var w in Uia.WindowsOf(Processes).Concat(Uia.TopWindows(w => w.Current.Name == "Clock")))
        {
            foreach (var n in Uia.Find(w, ControlType.Text))
            {
                var m = TimeText.Match(n.Name.Replace(" ", ""));
                if (!m.Success) continue;
                double s = (m.Groups[1].Success ? int.Parse(m.Groups[1].Value) * 3600 : 0)
                           + int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value)
                           + (m.Groups[4].Success ? int.Parse(m.Groups[4].Value) / 100.0 : 0);
                times.Add((i++, s, m.Groups[4].Success, n.Name));
            }
        }

        var texts = string.Join(" | ", times.Select(t => t.Text));
        if (texts != _loggedTexts && times.Count > 0 && times.Count < 12)
        {
            _loggedTexts = texts;
            Log.Write("clock sees: " + texts); // helps tune this on a real machine
        }

        // Moving numbers are running. Down = timer, up (usually with hundredths) = stopwatch.
        string? mode = null;
        double value = 0;
        foreach (var t in times)
        {
            if (!_last.TryGetValue(t.Index, out var before)) continue;
            if (t.Seconds < before - 0.2) { mode = "timer"; value = t.Seconds; break; }
            if (t.Seconds > before + 0.2) { mode = "stopwatch"; value = t.Seconds; }
        }
        _last = times.ToDictionary(t => t.Index, t => t.Seconds);

        bool done = mode == null && _lastRunningTimer is > 0 and <= 2.5;
        _lastRunningTimer = mode == "timer" ? value : -1;
        _ui.BeginInvoke(() =>
        {
            if (done) TimerDone?.Invoke();
            Apply(mode, mode == "timer" ? "Timer" : "Stopwatch", value);
        });
    }

    void Apply(string? mode, string? label, double seconds)
    {
        bool active = mode != null;
        if (active)
        {
            if (State.Mode != mode) State.Since = DateTime.Now;
            State.Mode = mode!;
            State.Label = label!;
            var t = TimeSpan.FromSeconds(seconds);
            State.Text = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        }
        if (active == State.IsActive) return;
        State.IsActive = active;
        ActiveChanged?.Invoke();
    }
}
