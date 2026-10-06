using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using DyIsd.Island;
using DyIsd.Native;

namespace DyIsd.Services;

public sealed record CallApp(string Name, string[] KeyHints, string[] Processes);

/// <summary>
/// You're on a call when a call app (WhatsApp, Discord, Teams, Zoom, Telegram) is using your
/// microphone. Shows an iPhone-style green call pill with a timer and your voice level.
/// </summary>
public sealed class CallService
{
    static readonly CallApp[] Apps =
    {
        new("WhatsApp", new[] { "whatsapp" }, new[] { "whatsapp", "whatsapp.root" }),
        new("Discord", new[] { "discord" }, new[] { "discord", "discordptb", "discordcanary" }),
        new("Teams", new[] { "msteams", "teams" }, new[] { "ms-teams", "teams" }),
        new("Zoom", new[] { "zoom" }, new[] { "zoom" }),
        new("Telegram", new[] { "telegram" }, new[] { "telegram" }),
    };

    public CallState State { get; } = new();
    public event Action? ActiveChanged;
    public CallApp? App { get; private set; }

    readonly PrivacyService _privacy;
    readonly MicService _mic;
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _meter = new() { Interval = TimeSpan.FromMilliseconds(60) };
    readonly DispatcherTimer _discordSync = new() { Interval = TimeSpan.FromSeconds(2) };
    bool _syncing;
    bool _weMutedMic;

    public CallService(PrivacyService privacy, MicService mic)
    {
        _privacy = privacy;
        _mic = mic;
        _privacy.Changed += Check;
        _mic.MuteChanged += muted => { if (App != null && App.Name != "Discord") State.Muted = muted; };
        _clock.Tick += (_, _) => Tick();
        // In a Discord call, read Discord's own Mute button so the island always matches it.
        _discordSync.Tick += async (_, _) =>
        {
            if (_syncing || App?.Name != "Discord") return;
            _syncing = true;
            try
            {
                var muted = await System.Threading.Tasks.Task.Run(DiscordControl.IsMuted);
                if (muted != null && App?.Name == "Discord") State.Muted = muted.Value;
            }
            finally { _syncing = false; }
        };
        _meter.Tick += (_, _) => State.Level = State.Muted ? 0 : Math.Min(1, _mic.Level * 2.2);
    }

    void Check()
    {
        var match = _privacy.MicApps
            .Select(m => Apps.FirstOrDefault(a => a.KeyHints.Any(h => m.Key.Contains(h))))
            .FirstOrDefault(a => a != null);

        if (match == App) return;
        if (match == null) End();
        else Begin(match);
    }

    void Begin(CallApp app)
    {
        App = app;
        State.AppName = app.Name;
        State.Since = DateTime.Now;
        State.Muted = app.Name != "Discord" && _mic.Muted;
        State.Who = FindWho(app);
        Tick();
        _clock.Start();
        _meter.Start();
        if (app.Name == "Discord") _discordSync.Start();
        State.IsActive = true;
        Log.Write($"call started: {app.Name} ({State.Who})");
        ActiveChanged?.Invoke();
    }

    void End()
    {
        Log.Write($"call ended: {App?.Name}");
        App = null;
        _clock.Stop();
        _meter.Stop();
        _discordSync.Stop();
        State.IsActive = false;
        State.Muted = false;
        // Never leave your mic muted after a call because of the island.
        if (_weMutedMic) _mic.SetMuted(false);
        _weMutedMic = false;
        ActiveChanged?.Invoke();
    }

    void Tick()
    {
        var t = DateTime.Now - State.Since;
        State.Elapsed = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        if (App != null && t.Seconds % 5 == 0) State.Who = FindWho(App); // the name can appear a bit later
    }

    /// <summary>
    /// Mute button on the island. In Discord it presses Discord's own Mute button (or your Discord
    /// shortcut if the button can't be found); everywhere else it mutes your mic for the whole PC.
    /// </summary>
    public async void ToggleMute(int discordKey, int discordMods)
    {
        if (App == null) return;
        if (App.Name == "Discord")
        {
            // Press Discord's own Mute button; your shortcut is the fallback.
            bool pressed = await System.Threading.Tasks.Task.Run(DiscordControl.ToggleMute);
            if (!pressed && discordKey != 0) Win32.PressShortcut(discordMods, discordKey);
            if (pressed || discordKey != 0) State.Muted = !State.Muted;
            return;
        }
        bool mute = !_mic.Muted;
        _mic.SetMuted(mute);
        _weMutedMic = mute;
        State.Muted = mute;
    }

    /// <summary>You pressed your Discord mute shortcut yourself.</summary>
    public void DiscordShortcutPressed()
    {
        if (App?.Name == "Discord") State.Muted = !State.Muted;
    }

    /// <summary>Best guess at who you're talking to, from the app's window titles.</summary>
    static string FindWho(CallApp app)
    {
        var titles = new List<string>();
        var pids = new HashSet<uint>();
        foreach (var name in app.Processes)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    pids.Add((uint)p.Id);
                    p.Dispose();
                }
            }
            catch { }
        }
        if (pids.Count == 0) return app.Name;

        Win32.EnumWindows((h, _) =>
        {
            if (!Win32.IsWindowVisible(h)) return true;
            Win32.GetWindowThreadProcessId(h, out uint pid);
            if (!pids.Contains(pid)) return true;
            var t = Win32.WindowTitle(h).Trim();
            if (t.Length > 0) titles.Add(t);
            return true;
        }, IntPtr.Zero);

        foreach (var t in titles)
        {
            string s = t;
            if (s.EndsWith(" - Discord")) s = s[..^" - Discord".Length];
            if (app.Name == "Discord")
            {
                // "#general | My Server" → "My Server";  "@Rahul" → "Rahul"
                int bar = s.LastIndexOf(" | ", StringComparison.Ordinal);
                if (bar >= 0) s = s[(bar + 3)..];
                s = s.TrimStart('@', '#');
            }
            if (s.Length > 0 && !s.Equals(app.Name, StringComparison.OrdinalIgnoreCase) && !s.Equals("Discord", StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return app.Name;
    }
}
