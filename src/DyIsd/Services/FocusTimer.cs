using System;
using System.Windows.Threading;
using DyIsd.Island;

namespace DyIsd.Services;

/// <summary>Pomodoro-style focus timer that lives in the island.</summary>
public sealed class FocusTimer : Observable
{
    public event Action? ActiveChanged;
    public event Action? Finished;

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    int _total, _left;
    bool _running;

    public FocusTimer() => _timer.Tick += (_, _) => Tick();

    public bool IsActive { get; private set; }
    public bool IsRunning => _running;
    public string RemainingText => Format(_left);
    public double Progress => _total == 0 ? 0 : (double)_left / _total;
    public string PauseText => _running ? "Pause" : "Resume";
    public string SubText => $"Focus session · {_total / 60} min";

    public void Start(int minutes)
    {
        _total = _left = Math.Max(1, minutes) * 60;
        _running = true;
        _timer.Start();
        RaiseAll();
        if (!IsActive)
        {
            IsActive = true;
            ActiveChanged?.Invoke();
        }
    }

    public void TogglePause()
    {
        _running = !_running;
        RaiseAll();
    }

    public void AddFive()
    {
        _left += 300;
        _total += 300;
        RaiseAll();
    }

    public void End()
    {
        _running = false;
        _timer.Stop();
        if (!IsActive) return;
        IsActive = false;
        ActiveChanged?.Invoke();
    }

    void Tick()
    {
        if (!_running) return;
        _left--;
        if (_left <= 0)
        {
            End();
            Finished?.Invoke();
            return;
        }
        RaiseAll();
    }

    void RaiseAll()
    {
        Raise(nameof(RemainingText));
        Raise(nameof(Progress));
        Raise(nameof(PauseText));
        Raise(nameof(SubText));
        Raise(nameof(IsRunning));
    }

    static string Format(int s) =>
        s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
}
