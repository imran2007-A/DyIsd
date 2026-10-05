using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using DyIsd.Island;
using DyIsd.Settings;

namespace DyIsd.Services;

/// <summary>
/// Spots an incoming WhatsApp / Discord / Teams / Telegram call and shows it on the island with
/// Answer and Decline, like an iPhone.
///
/// These apps don't tell other apps about calls, so DyIsd looks for the Accept and Decline
/// buttons in their windows (UI Automation, what screen readers use). To keep that cheap it
/// only looks when the app is making a sound (the ringtone) or has a window titled like a call.
/// </summary>
public sealed class RingService
{
    sealed record App(string Name, string[] Processes, Color Color);

    static readonly App[] Apps =
    {
        new("WhatsApp", new[] { "whatsapp", "whatsapp.root" }, Color.FromRgb(0x25, 0xD3, 0x66)),
        new("Discord", new[] { "discord", "discordptb", "discordcanary" }, Color.FromRgb(0x58, 0x65, 0xF2)),
        new("Teams", new[] { "ms-teams", "teams" }, Color.FromRgb(0x62, 0x64, 0xA7)),
        new("Telegram", new[] { "telegram" }, Color.FromRgb(0x2A, 0xAB, 0xEE)),
    };

    static readonly Regex AnswerRe = new(@"^(accept|answer|join call|join|pick up|accept call|accept voice call|accept video call|answer call)\b", RegexOptions.IgnoreCase);
    static readonly Regex DeclineRe = new(@"^(decline|reject|ignore|dismiss|decline call|reject call)\b", RegexOptions.IgnoreCase);
    static readonly Regex CallTitle = new(@"call|incoming|ringing|calling", RegexOptions.IgnoreCase);
    static readonly Regex NotAName = new(@"call|whatsapp|discord|teams|telegram|accept|decline|answer|reject|ignore|video|voice|incoming|ringing|mute|camera|minimi|maximi|close|settings|\d+:\d+|^\W*$", RegexOptions.IgnoreCase);

    public RingState State { get; } = new();
    public event Action? ActiveChanged;
    public string[] Processes { get; private set; } = Array.Empty<string>();

    readonly CallState _call;
    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    AutomationElement? _answer, _decline;
    bool _busy;
    int _misses, _ticks;
    string _logged = "";

    public RingService(CallState call) => _call = call;

    public void Start()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    void Poll()
    {
        if (_busy) return;
        if (!SettingsStore.Current.Features.Calls)
        {
            if (State.IsActive) Clear();
            return;
        }
        _ticks++;
        var running = Apps.Where(a => Uia.Running(a.Processes)).ToList();
        if (running.Count == 0)
        {
            if (State.IsActive) Clear();
            return;
        }
        _busy = true;
        bool checkTitles = _ticks % 5 == 0 || State.IsActive;
        Task.Run(() => Scan(running, checkTitles)).ContinueWith(t =>
        {
            _busy = false;
            if (t.Exception != null) Log.Error("ring", t.Exception.InnerException ?? t.Exception);
        });
    }

    void Scan(List<App> running, bool checkTitles)
    {
        var sounding = AudioSessions.SoundingProcessIds();
        foreach (var app in running)
        {
            var pids = new HashSet<int>();
            foreach (var name in app.Processes)
            {
                foreach (var p in Process.GetProcessesByName(name)) { pids.Add(p.Id); p.Dispose(); }
            }
            bool ringing = pids.Overlaps(sounding);
            foreach (var w in Uia.TopWindows(w => pids.Contains(w.Current.ProcessId)))
            {
                string title;
                try { title = w.Current.Name ?? ""; } catch { continue; }
                if (!ringing && !(checkTitles && CallTitle.IsMatch(title))) continue;

                var nodes = Uia.Find(w, ControlType.Button, ControlType.Text);
                var answer = nodes.FirstOrDefault(n => n.Type == ControlType.Button.ProgrammaticName && AnswerRe.IsMatch(n.Name));
                var decline = nodes.FirstOrDefault(n => n.Type == ControlType.Button.ProgrammaticName && DeclineRe.IsMatch(n.Name));
                var names = string.Join(" | ", nodes.Where(n => n.Type == ControlType.Button.ProgrammaticName).Select(n => n.Name).Take(15));
                if (names != _logged && (answer != null || decline != null))
                {
                    _logged = names;
                    Log.Write($"ring sees in {app.Name} \"{title}\": {names}"); // helps tune on a real machine
                }
                if (answer == null || decline == null) continue;
                if (_call.IsActive && answer.Name.StartsWith("join", StringComparison.OrdinalIgnoreCase)) continue; // already in it

                string who = nodes.Where(n => n.Type == ControlType.Text.ProgrammaticName)
                                  .Select(n => n.Name).FirstOrDefault(n => n.Length is >= 2 and <= 40 && !NotAName.IsMatch(n))
                             ?? (!NotAName.IsMatch(title) && title.Length is > 1 and < 40 ? title : app.Name);
                _ui.BeginInvoke(() => Show(app, who, answer.Element, decline.Element));
                return;
            }
        }
        _ui.BeginInvoke(() =>
        {
            if (!State.IsActive) return;
            if (++_misses >= 2) Clear(); // gone for two checks in a row: answered elsewhere or missed
        });
    }

    void Show(App app, string who, AutomationElement answer, AutomationElement decline)
    {
        _misses = 0;
        _answer = answer;
        _decline = decline;
        Processes = app.Processes;
        if (State.IsActive && State.AppName == app.Name && State.Who == who) return;
        State.AppName = app.Name;
        State.Who = who;
        State.AppColor = ThemeService.Solid(app.Color);
        if (!State.IsActive)
        {
            State.Since = DateTime.Now;
            State.IsActive = true;
            Log.Write($"ringing: {app.Name} ({who})");
            ActiveChanged?.Invoke();
        }
    }

    void Clear()
    {
        _answer = _decline = null;
        _misses = 0;
        if (!State.IsActive) return;
        State.IsActive = false;
        ActiveChanged?.Invoke();
    }

    public bool Answer() => Press(_answer, "answer");
    public bool Decline() => Press(_decline, "decline");

    bool Press(AutomationElement? button, string what)
    {
        if (!State.IsActive) return false;
        var procs = Processes;
        Task.Run(() =>
        {
            if (button == null || !Uia.Press(button))
            {
                Log.Write($"ring: couldn't press {what}, opening the app instead");
                _ui.BeginInvoke(() => AppJumper.Focus(procs));
            }
        });
        Clear();
        return true;
    }
}
