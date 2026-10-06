using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DyIsd.Settings;

public sealed class FeatureFlags
{
    public bool Media { get; set; } = true;
    public bool Volume { get; set; } = true;
    public bool Brightness { get; set; } = true;
    public bool Battery { get; set; } = true;
    public bool FocusTimer { get; set; } = true;
    public bool Clipboard { get; set; } = true;
    public bool Deadlines { get; set; } = true;
    public bool Downloads { get; set; } = true;
    /// <summary>Camera-in-use dot.</summary>
    public bool Privacy { get; set; } = true;
    public bool Calls { get; set; } = true;
    public bool Earbuds { get; set; } = true;
    /// <summary>The Windows Clock app's timer and stopwatch.</summary>
    public bool Clock { get; set; } = true;
    /// <summary>Bluetooth and Nearby Share file transfers.</summary>
    public bool Transfers { get; set; } = true;
}

public sealed class ManualDeadline
{
    public string Title { get; set; } = "";
    public DateTime Due { get; set; }

    /// <summary>Remind this many minutes before it's due (0 = at the due time).</summary>
    public List<int> RemindBeforeMinutes { get; set; } = new() { 1440, 120, 30 };

    /// <summary>"none", "daily" or "weekly": keep nudging until it's due.</summary>
    public string Repeat { get; set; } = "none";

    /// <summary>Time of day for daily/weekly nudges.</summary>
    public TimeSpan RepeatAt { get; set; } = new(9, 0, 0);

    /// <summary>Extra one-off reminders on specific dates.</summary>
    public List<DateTime> RemindOn { get; set; } = new();

    public DateTime Created { get; set; } = DateTime.Now;
}

public sealed class JarvisPhrase
{
    /// <summary>What you say, e.g. "lab mode".</summary>
    public string Say { get; set; } = "";
    /// <summary>What Jarvis does, written as you'd say it: "open vs code and open chrome and play apple music".</summary>
    public string Do { get; set; } = "";
}

/// <summary>One step of a mode, e.g. ("playlist", "Chill Vibes") or ("volume", "30").</summary>
public sealed class ModeStep
{
    /// <summary>open, close, playlist, song, youtube, volume, brightness, focus, theme, wifi, bluetooth, wait, type, say.</summary>
    public string Kind { get; set; } = "say";
    public string Value { get; set; } = "";
}

/// <summary>A mode: say its name (or press ▶ in the Control Center) and Jarvis runs its steps in order.</summary>
public sealed class JarvisMode
{
    public string Name { get; set; } = "";
    public List<ModeStep> Steps { get; set; } = new();
    /// <summary>"End … mode" closes the apps it opened and pauses the music.</summary>
    public bool CloseOnEnd { get; set; } = true;
}

public sealed class AppSettings
{
    /// <summary>"left", "center" or "right".</summary>
    public string Position { get; set; } = "center";

    public FeatureFlags Features { get; set; } = new();
    public bool HideWindowsVolumePopup { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;
    public bool ClipboardShowText { get; set; } = true;
    public int FocusMinutes { get; set; } = 25;
    public string CalendarUrl { get; set; } = "";

    /// <summary>Your Discord "toggle mute" shortcut (0 = not set). Modifiers use Win32.MOD_KEY_* flags.</summary>
    public int DiscordMuteKey { get; set; }
    public int DiscordMuteModifiers { get; set; }
    public List<ManualDeadline> ManualDeadlines { get; set; } = new();
    public bool FirstRunDone { get; set; }

    /// <summary>Jarvis voice control (hold Ctrl + Space).</summary>
    public bool JarvisEnabled { get; set; } = true;
    /// <summary>Also listen for "Jarvis" all the time (keeps the microphone on).</summary>
    public bool JarvisWakeWord { get; set; } = true;
    /// <summary>Soft sounds when Jarvis starts listening and answers.</summary>
    public bool JarvisSounds { get; set; } = true;
    /// <summary>Hold this key (with JarvisMods held too) to talk. Default Ctrl + Space.</summary>
    public int JarvisKey { get; set; } = 0x20;
    /// <summary>Win32.MOD_KEY_* flags that must be held with JarvisKey.</summary>
    public int JarvisMods { get; set; } = 2;
    /// <summary>Old "your phrases"; turned into modes on load.</summary>
    public List<JarvisPhrase> JarvisPhrases { get; set; } = new();
    /// <summary>Your modes ("Claude mode": open Claude, play my playlist…).</summary>
    public List<JarvisMode> JarvisModes { get; set; } = new();
}

/// <summary>Loads and saves settings to %LOCALAPPDATA%\DyIsd\settings.json.</summary>
public static class SettingsStore
{
    public static AppSettings Current { get; private set; } = new();

    /// <summary>Raised after every save so the app can apply the change.</summary>
    public static event Action? Changed;

    static string FilePath => Path.Combine(Log.Dir, "settings.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("settings load", ex);
            Current = new AppSettings();
        }

        Current.Features ??= new FeatureFlags();
        Current.ManualDeadlines ??= new List<ManualDeadline>();
        Current.JarvisPhrases ??= new List<JarvisPhrase>();
        Current.JarvisModes ??= new List<JarvisMode>();
        foreach (var m in Current.JarvisModes) m.Steps ??= new List<ModeStep>();
        // Phrases from the earlier version become one-step modes.
        foreach (var p in Current.JarvisPhrases)
            if (p.Say.Trim().Length > 0 && !Current.JarvisModes.Exists(m => string.Equals(m.Name, p.Say, StringComparison.OrdinalIgnoreCase)))
                Current.JarvisModes.Add(new JarvisMode { Name = p.Say.Trim(), Steps = { new ModeStep { Kind = "say", Value = p.Do } }, CloseOnEnd = false });
        Current.JarvisPhrases.Clear();
        if (Current.JarvisKey == 0) { Current.JarvisKey = 0x20; Current.JarvisMods = 2; }
        Current.FocusMinutes = Math.Clamp(Current.FocusMinutes, 1, 180);
        if (Current.Position is not ("left" or "center" or "right")) Current.Position = "center";
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error("settings save", ex);
        }
        Changed?.Invoke();
    }
}
