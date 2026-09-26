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
    public bool Privacy { get; set; } = true;
    public bool Earbuds { get; set; } = true;
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
    public List<ManualDeadline> ManualDeadlines { get; set; } = new();
    public bool FirstRunDone { get; set; }
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
