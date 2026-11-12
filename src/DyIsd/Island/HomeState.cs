using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DyIsd.Services;
using DyIsd.Settings;

namespace DyIsd.Island;

/// <summary>The face shown when nothing else is on.</summary>
public sealed class FaceState : Observable
{
    Controls.Mood _mood = Controls.Mood.Idle;
    public Controls.Mood Mood { get => _mood; set => Set(ref _mood, value); }
}

public sealed record ClipItem(string Text, string Short, string When);
public sealed record NoteItem(string App, string Title, string Body, string When);

public sealed class TrayItem
{
    public string Path { get; init; } = "";
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\'));
    public ImageSource? Icon { get; init; }
    public bool Exists => File.Exists(Path) || Directory.Exists(Path);
}

/// <summary>
/// The Home panel (Win + Shift + H, or click the face): today's date, cards for Claude Code,
/// music and focus, and tabs for Claude, music, notifications, timers, a file tray, clipboard
/// history and notes.
/// </summary>
public sealed class HomeState : Observable
{
    public static readonly string[] Tabs = { "home", "claude", "music", "rem", "notes-in", "timer", "tray", "clip", "notes" };

    string _tab = "home", _date = "", _time = "", _notes = "", _notifStatus = "";
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly DispatcherTimer _saveNotes = new() { Interval = TimeSpan.FromSeconds(1) };

    public HomeState(ClaudeState claude, MediaState media, FocusTimer focus, ReminderStore reminders)
    {
        Reminders = reminders;
        reminders.Changed += () => { RaiseCards(); Raise(nameof(NoReminders)); };
        Claude = claude;
        Media = media;
        Focus = focus;
        _clock.Tick += (_, _) => UpdateClock();
        _saveNotes.Tick += (_, _) =>
        {
            _saveNotes.Stop();
            try { File.WriteAllText(NotesPath, _notes); } catch (Exception ex) { Log.Error("notes save", ex); }
        };
        try { _notes = File.Exists(NotesPath) ? File.ReadAllText(NotesPath) : ""; } catch { }
        foreach (var saved in SettingsStore.Current.TrayFiles)
        {
            var p = saved;
            // Moved from the old tray folder (inside DyIsd's app data) to Documents\DyIsd Tray.
            if (File.Exists(p) && p.StartsWith(System.IO.Path.Combine(Log.Dir, "tray"), StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.CreateDirectory(TrayDir); var to = VirtualFiles.Unique(TrayDir, System.IO.Path.GetFileName(p)); File.Move(p, to); p = to; } catch { }
            }
            if (File.Exists(p) || Directory.Exists(p)) Tray.Add(MakeTray(p)); // drop ones that vanished
        }
        if (!SettingsStore.Current.TrayFiles.SequenceEqual(Tray.Select(t => t.Path))) { SettingsStore.Current.TrayFiles = Tray.Select(t => t.Path).ToList(); SettingsStore.Save(); }
        UpdateClock();
        claude.PropertyChanged += (_, _) => RaiseCards();
        media.PropertyChanged += (_, _) => RaiseCards();
        focus.PropertyChanged += (_, _) => RaiseCards();
        focus.ActiveChanged += RaiseCards;
    }

    // ---------- the three cards ----------

    public string ClaudeBig => !Claude.Connected ? "Connect" : Claude.IsActive ? Claude.Status : "Idle";
    public string ClaudeSub => !Claude.Connected ? "Show Claude Code here" : Claude.IsActive ? (Claude.Task.Length > 0 ? Claude.Task : Claude.Project) : "No session running";
    public string ClaudeIdle => Claude.Connected ? "No Claude Code session running" : "Connect Claude Code to see what it's doing";

    public Brush MusicTint => Media.ArtColor is Color c ? Services.ThemeService.Solid(c) : DefaultTint;
    static readonly Brush DefaultTint = Services.ThemeService.Solid(Color.FromRgb(0xC8, 0x1D, 0x3E));
    public string MusicBig => Media.HasSession && Media.Title.Length > 0 ? Media.Title : "Nothing playing";
    public string MusicSub => !Media.HasSession ? "" : Media.IsPlaying ? Media.Artist : "paused";

    // Reminders card: how many are left and the next one.
    public ReminderStore Reminders { get; }
    public bool NoReminders => Reminders.Items.Count == 0;
    public string RemBig => Reminders.Left == 0 ? "All done" : $"{Reminders.Left} to do";
    public string RemSub => Reminders.Items.FirstOrDefault(r => !r.Done) is { } r ? r.Text + (r.WhenText.Length > 0 ? " · " + r.WhenText : "") : "Add one, or ask Jarvis";

    public bool FocusOn => Focus.IsActive;
    public string FocusBig => Focus.IsActive ? Focus.RemainingText : $"{SettingsStore.Current.FocusMinutes}:00";
    public string FocusSub => !Focus.IsActive ? "Start a session" : Focus.IsRunning ? $"{(Focus.SecondsLeft + 59) / 60} min left" : "paused";
    public string FocusGlyph => Focus.IsActive ? "\uE71A" : "\uE768";
    public string FocusTag => Focus.IsActive ? "tend" : "home-focus";
    public string FocusTip => Focus.IsActive ? "End focus" : "Start focus";

    string _volumeGlyph = "\uE995";
    public string VolumeGlyph { get => _volumeGlyph; set => Set(ref _volumeGlyph, value); }

    void RaiseCards()
    {
        foreach (var n in new[] { nameof(ClaudeBig), nameof(ClaudeSub), nameof(ClaudeIdle), nameof(MusicTint), nameof(MusicBig), nameof(MusicSub),
                                  nameof(RemBig), nameof(RemSub), nameof(FocusOn), nameof(FocusBig), nameof(FocusSub), nameof(FocusGlyph), nameof(FocusTag), nameof(FocusTip) }) Raise(n);
    }

    public ClaudeState Claude { get; }
    public MediaState Media { get; }
    public FocusTimer Focus { get; }

    public string Tab { get => _tab; set { if (Set(ref _tab, value)) RaiseTabs(); } }
    public bool IsHome => _tab == "home";
    public bool IsClaude => _tab == "claude";
    public bool IsMusic => _tab == "music";
    public bool IsNotifs => _tab == "notes-in";
    public bool IsTimer => _tab == "timer";
    public bool IsTray => _tab == "tray";
    public bool IsClip => _tab == "clip";
    public bool IsNotes => _tab == "notes";

    void RaiseTabs()
    {
        foreach (var n in new[] { nameof(IsHome), nameof(IsClaude), nameof(IsMusic), nameof(IsNotifs), nameof(IsTimer), nameof(IsTray), nameof(IsClip), nameof(IsNotes) }) Raise(n);
    }

    public string Date { get => _date; set => Set(ref _date, value); }
    public string Time { get => _time; set => Set(ref _time, value); }

    /// <summary>The clock only ticks while the panel is open.</summary>
    public void SetOpen(bool open)
    {
        if (open) { UpdateClock(); _clock.Start(); } else _clock.Stop();
    }

    void UpdateClock()
    {
        var now = DateTime.Now;
        Date = now.ToString("ddd d MMM yyyy");
        Time = now.ToString("HH:mm");
    }

    // ---------- clipboard history ----------

    public ObservableCollection<ClipItem> Clips { get; } = new();
    public bool NoClips => Clips.Count == 0;

    public void AddClip(ClipInfo c)
    {
        if (c.Kind != ClipKind.Text) return;
        var oneLine = System.Text.RegularExpressions.Regex.Replace(c.Text, @"\s+", " ").Trim();
        if (oneLine.Length == 0) return;
        var old = Clips.FirstOrDefault(x => x.Text == c.Text);
        if (old != null) Clips.Remove(old);
        Clips.Insert(0, new ClipItem(c.Text.Length > 20000 ? c.Text[..20000] : c.Text, oneLine.Length > 70 ? oneLine[..68] + "…" : oneLine, DateTime.Now.ToString("HH:mm")));
        while (Clips.Count > 12) Clips.RemoveAt(Clips.Count - 1);
        Raise(nameof(NoClips));
    }

    // ---------- file tray ----------

    public ObservableCollection<TrayItem> Tray { get; } = new();
    public bool NoTray => Tray.Count == 0;

    /// <summary>
    /// In Documents, not DyIsd's app-data folder: other apps (Explorer for the installed DyIsd,
    /// packaged apps like Notepad always) can't see into that one, so a file dragged out of the
    /// tray arrived as "path not found".
    /// </summary>
    public static string TrayDir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DyIsd Tray");

    /// <summary>
    /// Files are copied into DyIsd's own tray folder, so they're still there when the app they
    /// came from cleans up (a file dragged out of WhatsApp is a temporary copy that vanishes).
    /// Folders and files over 1 GB stay where they are.
    /// </summary>
    public void AddToTray(string[] paths)
    {
        foreach (var src in paths)
        {
            string p = src;
            try
            {
                if (File.Exists(src) && !src.StartsWith(TrayDir, StringComparison.OrdinalIgnoreCase) && new FileInfo(src).Length < (1L << 30))
                {
                    Directory.CreateDirectory(TrayDir);
                    p = System.IO.Path.Combine(TrayDir, System.IO.Path.GetFileName(src));
                    for (int n = 2; File.Exists(p); n++)
                        p = System.IO.Path.Combine(TrayDir, $"{System.IO.Path.GetFileNameWithoutExtension(src)} ({n}){System.IO.Path.GetExtension(src)}");
                    File.Copy(src, p);
                }
            }
            catch (Exception ex) { Log.Error("tray copy", ex); p = src; }
            if (Tray.Any(t => string.Equals(t.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            Tray.Insert(0, MakeTray(p));
        }
        while (Tray.Count > 8) Tray.RemoveAt(Tray.Count - 1);
        SaveTray();
    }

    public void RemoveFromTray(string path)
    {
        var t = Tray.FirstOrDefault(x => x.Path == path);
        if (t != null) Tray.Remove(t);
        // Our own copy goes too; the original (if it was outside the tray folder) is untouched.
        try { if (path.StartsWith(TrayDir, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) File.Delete(path); } catch { }
        SaveTray();
    }

    void SaveTray()
    {
        SettingsStore.Current.TrayFiles = Tray.Select(t => t.Path).ToList();
        SettingsStore.Save();
        Raise(nameof(NoTray));
    }

    static TrayItem MakeTray(string path)
    {
        ImageSource? icon = null;
        try
        {
            if (File.Exists(path))
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico != null)
                {
                    icon = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                    icon.Freeze();
                }
            }
        }
        catch { }
        return new TrayItem { Path = path, Icon = icon };
    }

    // ---------- notes ----------

    static string NotesPath => System.IO.Path.Combine(Log.Dir, "notes.txt");

    /// <summary>A scratchpad, saved a second after you stop typing.</summary>
    public string Notes
    {
        get => _notes;
        set
        {
            if (!Set(ref _notes, value)) return;
            _saveNotes.Stop();
            _saveNotes.Start();
        }
    }

    // ---------- notifications ----------

    public ObservableCollection<NoteItem> Notifications { get; } = new();
    /// <summary>Why the list is empty (no permission, only in the installed app…), or "".</summary>
    public string NotifStatus { get => _notifStatus; set { if (Set(ref _notifStatus, value)) Raise(nameof(NoNotifs)); } }
    public bool NoNotifs => Notifications.Count == 0;

    public void SetNotifications(System.Collections.Generic.IEnumerable<NoteItem> items, string status)
    {
        Notifications.Clear();
        foreach (var n in items.Take(12)) Notifications.Add(n);
        NotifStatus = Notifications.Count == 0 && status.Length == 0 ? "No notifications" : status;
        Raise(nameof(NoNotifs));
    }
}
