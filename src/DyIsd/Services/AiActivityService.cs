using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;

namespace DyIsd.Services;

public sealed record AiApp(string Key, string Name, string[] Processes, Color Color);

/// <summary>
/// Guesses when Claude, ChatGPT or Codex is busy working. These apps give no official signal,
/// so we watch how much CPU they use: writing a long answer keeps them busy, idle apps are quiet.
/// It's a smart guess, so thresholds live at the top of this file for tuning.
/// </summary>
public sealed class AiActivityService
{
    public static readonly AiApp[] Apps =
    {
        new("claude", "Claude", new[] { "claude" }, Color.FromRgb(0xD9, 0x77, 0x57)),
        new("chatgpt", "ChatGPT", new[] { "chatgpt" }, Color.FromRgb(0x10, 0xA3, 0x7F)),
        new("codex", "Codex", new[] { "codex" }, Color.FromRgb(0xE8, 0xE8, 0xE8)),
    };

    // Percent of one CPU core, averaged over the last few seconds.
    const double StartAbove = 8.0;   // busier than this for ~3s = "working"
    const double StopBelow = 2.5;    // quieter than this for ~5s = "finished"
    const int StartSamples = 3, StopSamples = 5;

    public event Action<AiApp>? Started;
    /// <summary>The app and how long it worked.</summary>
    public event Action<AiApp, TimeSpan>? Finished;

    public AiApp? Working => _state.Values.Where(s => s.Working).OrderByDescending(s => s.Since).Select(s => s.App).FirstOrDefault();
    public DateTime WorkingSince(AiApp app) => _state[app.Key].Since;

    sealed class State
    {
        public required AiApp App;
        public TimeSpan LastCpu;
        public DateTime LastSample;
        public readonly Queue<double> Samples = new();
        public bool Working;
        public DateTime Since;
        public DateTime FirstSeen = DateTime.MinValue;
    }

    readonly Dictionary<string, State> _state = Apps.ToDictionary(a => a.Key, a => new State { App = a });
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public void Start()
    {
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
    }

    void Sample()
    {
        var now = DateTime.UtcNow;
        foreach (var s in _state.Values)
        {
            var cpu = TotalCpu(s.App.Processes, out bool running);
            if (!running)
            {
                if (s.Working) Stop(s, now);
                s.FirstSeen = DateTime.MinValue;
                s.Samples.Clear();
                s.LastSample = default;
                continue;
            }
            if (s.FirstSeen == DateTime.MinValue) s.FirstSeen = now;

            if (s.LastSample != default)
            {
                double pct = (cpu - s.LastCpu).TotalMilliseconds / (now - s.LastSample).TotalMilliseconds * 100;
                s.Samples.Enqueue(Math.Max(0, pct));
                while (s.Samples.Count > StopSamples) s.Samples.Dequeue();
            }
            s.LastCpu = cpu;
            s.LastSample = now;

            // Ignore the first 20 seconds after the app opens (start-up is always busy).
            if (now - s.FirstSeen < TimeSpan.FromSeconds(20)) continue;

            var recent = s.Samples.Reverse().ToArray();
            if (!s.Working && recent.Length >= StartSamples && recent.Take(StartSamples).All(v => v > StartAbove))
            {
                s.Working = true;
                s.Since = DateTime.Now;
                Log.Write($"ai: {s.App.Name} working ({string.Join(", ", recent.Select(v => v.ToString("0")))}% cpu)");
                Started?.Invoke(s.App);
            }
            else if (s.Working && recent.Length >= StopSamples && recent.All(v => v < StopBelow))
            {
                Stop(s, now);
            }
        }
    }

    void Stop(State s, DateTime now)
    {
        s.Working = false;
        var took = DateTime.Now - s.Since;
        Log.Write($"ai: {s.App.Name} finished after {took.TotalSeconds:0}s");
        Finished?.Invoke(s.App, took);
    }

    static TimeSpan TotalCpu(string[] names, out bool running)
    {
        var total = TimeSpan.Zero;
        running = false;
        foreach (var name in names)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { continue; }
            foreach (var p in procs)
            {
                try
                {
                    total += p.TotalProcessorTime;
                    running = true;
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        return total;
    }
}
