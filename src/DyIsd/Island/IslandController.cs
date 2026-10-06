using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Threading;
using DyIsd.Services;
using DyIsd.Settings;

namespace DyIsd.Island;

/// <summary>
/// Decides what the island shows. Two kinds of things:
///  - Activities that last: a call, a ringing call, music, timers, downloads, file transfers.
///    The most important one fills the island (call → ringing / Clock timer → music →
///    stopwatch / focus → downloads / transfers); the next one sits in a small circle beside it,
///    like iPhone. Click the circle to swap them. Never shown while you're in the app it belongs
///    to. Flick the island up to hide an activity until it changes (next song, new call...).
///  - Pop-ups that come and go: volume, silent switch, clipboard, battery, Jarvis...
///    They take over for a few seconds, then the island goes back to the activity or hides.
/// </summary>
public sealed class IslandController
{
    public event Action<string>? ActionRequested;
    /// <summary>Right-click or an open button: process names of the app to bring to the front.</summary>
    public event Action<string[]>? JumpRequested;
    public event Action<int, bool>? WheelRequested;
    public event Action<string>? PositionDropped;

    readonly IslandWindow _win;
    readonly MediaState _media;
    readonly FocusTimer _timer;
    readonly DownloadState _download;
    readonly CallState _call;
    readonly ClockState _clock;
    readonly RingState _ring;
    readonly TransferState _xfer;
    readonly ForegroundWatcher _fg;

    readonly DispatcherTimer _popupTimer = new();
    readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    readonly LevelInfo _volume = new(), _brightness = new();

    sealed record Popup(string Template, string Kind, object Data, double W, double H, bool FullscreenOk, string[]? JumpTo);
    Popup? _popup;
    string? _shownKind;
    bool _expanded, _hovering, _fullscreen, _paused;
    /// <summary>You paused from the island's player: keep a tiny dot so you can resume.</summary>
    bool _pausedHere;
    public string[] CallProcesses { get; set; } = Array.Empty<string>();
    public string[] RingProcesses { get; set; } = Array.Empty<string>();

    /// <summary>Flicked away: activity kind → what it was showing when you flicked it.</summary>
    readonly Dictionary<string, string> _dismissed = new();
    /// <summary>You clicked the circle: show this one big while these two are the top two.</summary>
    string? _preferred;
    (string, string)? _preferredPair;
    string? _bubbleKind;

    public static readonly string[] ClockProcesses = { "time" };
    public static readonly string[] TransferProcesses = { "fsquirt" };

    public string? LastDownloadPath { get; private set; }
    public (Brush? Brush, string? App) Privacy { get; set; }

    static AppSettings S => SettingsStore.Current;

    public IslandController(IslandWindow win, MediaState media, FocusTimer timer, DownloadState download, CallState call,
        ClockState clock, RingState ring, TransferState xfer, ForegroundWatcher fg)
    {
        _win = win;
        _media = media;
        _timer = timer;
        _download = download;
        _call = call;
        _clock = clock;
        _ring = ring;
        _xfer = xfer;
        _fg = fg;
        // A flicked-away song comes back when the next one starts.
        _media.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MediaState.Title) && _dismissed.ContainsKey("media")) Render();
        };
        // Playing again (from anywhere) or the player closing clears the paused dot.
        _media.PropertyChanged += (_, e) =>
        {
            if (!_pausedHere) return;
            if ((e.PropertyName == nameof(MediaState.IsPlaying) && _media.IsPlaying) ||
                (e.PropertyName == nameof(MediaState.HasSession) && !_media.HasSession))
            {
                _pausedHere = false;
                Render();
            }
        };

        _popupTimer.Tick += (_, _) => EndPopup();
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (_hovering || !_expanded) return;
            _expanded = false;
            Render();
        };

        _win.HoverChanged += OnHover;
        _win.Clicked += OnClick;
        _win.RightClicked += () =>
        {
            if (_popup != null) { EndPopup(); return; }
            Jump();
        };
        _win.ActionClicked += tag =>
        {
            if (tag == "jump") { Jump(); return; }
            if (tag.StartsWith("jump:")) { EndPopup(); JumpRequested?.Invoke(tag[5..].Split(',')); return; }
            if (tag is "dismiss" or "open-file" or "show-file" or "batt-settings") EndPopup();
            if (tag == "toggle" && _media.IsPlaying) _pausedHere = true; // pausing from the island
            ActionRequested?.Invoke(tag);
        };
        _win.Wheel += (dir, shift) => WheelRequested?.Invoke(dir, shift);
        _win.Seek += v => ActionRequested?.Invoke("seek:" + v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _win.VolumeSet += v => ActionRequested?.Invoke("volume:" + v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _win.Dropped += zone => PositionDropped?.Invoke(zone);
        _win.FlickedUp += OnFlick;
        _win.BubbleClicked += OnBubbleClick;
    }

    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (value) _popup = null;
            Render();
        }
    }

    public void SetFullscreen(bool fullscreen)
    {
        _fullscreen = fullscreen;
        if (Quiet && _popup is { FullscreenOk: false }) _popup = null;
        Render();
    }

    bool Quiet => _fullscreen && S.HideInFullscreen;

    // ---------------- which activity to show ----------------

    /// <summary>
    /// Running activities whose app you're not using, most important first.
    /// Same importance: the newest first.
    /// </summary>
    List<string> PickActivities()
    {
        var list = new List<(string Kind, int Rank, DateTime Since)>();
        if (Quiet) return new List<string>();
        var f = S.Features;
        if (f.Calls && _call.IsActive && !_fg.IsAny(CallProcesses)) list.Add(("call", 0, _call.Since));
        if (f.Calls && _ring.IsActive && !_fg.IsAny(RingProcesses)) list.Add(("ring", 1, _ring.Since));
        if (f.Clock && _clock.IsActive && !_fg.IsAny(ClockProcesses)) list.Add(("clock", _clock.Mode == "timer" ? 1 : 3, _clock.Since));
        bool mediaOn = _media.IsActive || (_pausedHere && _media.HasSession);
        if (f.Media && mediaOn && !_fg.IsAny(_media.Processes)) list.Add(("media", 2, _media.ActiveSince));
        if (f.FocusTimer && _timer.IsActive) list.Add(("timer", 3, _timer.StartedAt));
        if (f.Downloads && _download.IsActive && !_fg.IsAny(AppJumper.Browsers)) list.Add(("download", 4, _download.ActiveSince));
        if (f.Transfers && _xfer.IsActive && !_fg.IsAny(TransferProcesses)) list.Add(("xfer", 4, _xfer.Since));

        // Flicked away and still the same thing: keep it hidden. Changed: forget the flick.
        foreach (var kind in _dismissed.Keys.ToList())
            if (_dismissed[kind] != Signature(kind)) _dismissed.Remove(kind);
        list.RemoveAll(a => _dismissed.ContainsKey(a.Kind));

        var ordered = list.OrderBy(a => a.Rank).ThenByDescending(a => a.Since).Select(a => a.Kind).ToList();
        if (ordered.Count >= 2 && _preferred != null && _preferredPair is var (a, b) &&
            ((ordered[0] == a && ordered[1] == b) || (ordered[0] == b && ordered[1] == a)))
        {
            if (ordered[1] == _preferred) (ordered[0], ordered[1]) = (ordered[1], ordered[0]);
        }
        else
        {
            _preferred = null;
            _preferredPair = null;
        }
        return ordered;
    }

    /// <summary>What an activity is showing right now; a flick hides it until this changes.</summary>
    string Signature(string kind) => kind switch
    {
        "media" => _media.Title + "|" + _media.Artist,
        "call" => _call.Since.Ticks.ToString(),
        "ring" => _ring.Since.Ticks.ToString(),
        "clock" => _clock.Mode + _clock.Since.Ticks,
        "timer" => _timer.StartedAt.Ticks.ToString(),
        "download" => _download.FileName,
        "xfer" => _xfer.Since.Ticks.ToString(),
        _ => "",
    };

    public void Render()
    {
        if (_paused)
        {
            _win.HideIsland();
            _win.SetBubble(null, null, 0);
            _win.SetPrivacyDot(null, null);
            _shownKind = null;
            return;
        }

        var activities = PickActivities();
        var kind = activities.FirstOrDefault();
        var second = activities.Skip(1).FirstOrDefault();
        if (kind != _shownKind) _expanded = false;
        _shownKind = kind;

        double? width = null;
        string? bubble = null;
        if (_popup != null)
        {
            _win.ShowView(_popup.Template, _popup.Kind, _popup.Data, _popup.W, _popup.H);
            width = _popup.W;
        }
        else if (kind != null)
        {
            var (template, data, w, h) = Spec(kind, _expanded);
            _win.ShowView(template, template, data, w, h);
            width = w;
            // The second activity in a small circle beside a compact island.
            if (second != null && !_expanded && kind != "ring") bubble = second;
        }
        else
        {
            _win.HideIsland();
        }

        _bubbleKind = bubble;
        if (bubble != null)
        {
            var (bt, bd) = BubbleSpec(bubble);
            _win.SetBubble(bt, bd, width ?? 0);
        }
        else _win.SetBubble(null, null, width ?? 0);

        bool dot = S.Features.Privacy && Privacy.Brush != null && !Quiet;
        _win.SetPrivacyDot(dot ? Privacy.Brush : null, width == null ? null : width + (bubble != null ? IslandWindow.BubbleSize + IslandWindow.BubbleGap : 0));
    }

    (string, object, double, double) Spec(string kind, bool expanded) => kind switch
    {
        "media" => expanded ? ("media-e", _media, 366, 208)
                 : _pausedHere && !_media.IsPlaying ? ("media-dot", _media, 14, 14)
                 : ("media-c", _media, 190, 34),
        "call" => expanded ? ("call-e", _call, 360, 84) : ("call-c", _call, 190, 34),
        "ring" => ("ring-e", _ring, 370, 76), // a ringing call always shows big, like iPhone
        "timer" => expanded ? ("timer-e", _timer, 330, 128) : ("timer-c", _timer, 150, 34),
        "clock" => expanded ? ("clock-e", _clock, 300, 96) : ("clock-c", _clock, 150, 34),
        "xfer" => expanded ? ("xfer-e", _xfer, 346, 94) : ("xfer-c", _xfer, 150, 34),
        _ => expanded ? ("dl-e", _download, 346, 94) : ("dl-c", _download, 150, 34),
    };

    (string, object) BubbleSpec(string kind) => kind switch
    {
        "media" => ("b-media", _media),
        "call" => ("b-call", _call),
        "ring" => ("b-ring", _ring),
        "timer" => ("b-timer", _timer),
        "clock" => ("b-clock", _clock),
        "xfer" => ("b-xfer", _xfer),
        _ => ("b-dl", _download),
    };

    void OnBubbleClick()
    {
        if (_bubbleKind == null || _shownKind == null) return;
        _preferred = _bubbleKind;
        _preferredPair = (_shownKind, _bubbleKind);
        _expanded = false;
        Render();
    }

    /// <summary>"Bring it back": shows everything you flicked away.</summary>
    public void RestoreDismissed()
    {
        _dismissed.Clear();
        Render();
    }

    /// <summary>Flicked up: a pop-up closes; an activity hides until it changes.</summary>
    void OnFlick()
    {
        if (_popup != null) { EndPopup(); return; }
        if (_shownKind == null) return;
        _dismissed[_shownKind] = Signature(_shownKind);
        _expanded = false;
        Render();
    }

    // ---------------- mouse ----------------

    void OnHover(bool inside)
    {
        _hovering = inside;
        if (_popup != null)
        {
            // Resting the mouse on a pop-up keeps it on screen.
            _popupTimer.Stop();
            if (!inside)
            {
                _popupTimer.Interval = TimeSpan.FromMilliseconds(1400);
                _popupTimer.Start();
            }
            return;
        }
        if (!inside && _expanded) _collapseTimer.Start();
        else _collapseTimer.Stop();
    }

    /// <summary>Click: open (expand) the activity; click again to close. A pop-up closes.</summary>
    void OnClick()
    {
        if (_popup != null)
        {
            if (_popup.JumpTo != null) { var to = _popup.JumpTo; EndPopup(); JumpRequested?.Invoke(to); }
            else EndPopup();
            return;
        }
        if (_shownKind == null) return;
        if (_shownKind == "ring") { Jump(); return; }
        _expanded = !_expanded;
        Render();
    }

    /// <summary>Right-click (or the open button): go to the app the activity belongs to.</summary>
    void Jump()
    {
        string[]? to = _shownKind switch
        {
            "media" => _media.Processes,
            "call" => CallProcesses,
            "ring" => RingProcesses,
            "clock" => ClockProcesses,
            "xfer" => TransferProcesses,
            "download" => AppJumper.Browsers,
            _ => null,
        };
        if (to == null) return; // the focus timer has no app
        _expanded = false;
        JumpRequested?.Invoke(to);
    }

    // ---------------- pop-ups ----------------

    void ShowPopup(string template, string kind, object data, double w, double h, int ms, bool fullscreenOk = false, string[]? jumpTo = null)
    {
        if (_paused) return;
        if (Quiet && !fullscreenOk) return;
        _popup = new Popup(template, kind, data, w, h, fullscreenOk, jumpTo);
        _expanded = false;
        _popupTimer.Stop();
        _popupTimer.Interval = TimeSpan.FromMilliseconds(ms);
        if (!_hovering) _popupTimer.Start();
        Render();
    }

    public void EndPopup()
    {
        _popupTimer.Stop();
        if (_popup == null) return;
        _popup = null;
        Render();
    }

    static Brush B(string key) => ThemeService.Brush(key);

    public void ShowVolume(float level, bool muted)
    {
        _media.Volume = muted ? 0 : level;
        // The open player already has a volume slider: no extra pop-up.
        if (_expanded && _shownKind == "media" && _popup == null) return;
        _volume.Glyph = VolumeService.GlyphFor(level, muted);
        _volume.Value = muted ? 0 : level;
        _volume.Text = muted ? "Mute" : ((int)Math.Round(level * 100)).ToString();
        ShowPopup("level", "volume", _volume, 270, 40, 1600, fullscreenOk: true);
    }

    public void ShowBrightness(int level)
    {
        _brightness.Glyph = "\uE706";
        _brightness.Value = level / 100.0;
        _brightness.Text = level.ToString();
        ShowPopup("level", "brightness", _brightness, 270, 40, 1600, fullscreenOk: true);
    }

    public void ShowBattery(BatteryEvent ev, int pct, string? timeLeft)
    {
        switch (ev)
        {
            case BatteryEvent.Charging:
                ShowPopup("pill", "battery", new InfoCard
                {
                    Glyph = "\uE945", GlyphBrush = B("Good"), Title = "Charging", Right = pct + "%", RightBrush = B("Good"),
                }, 210, 40, 2800);
                break;
            case BatteryEvent.Unplugged:
                ShowPopup("pill", "battery", new InfoCard
                {
                    Glyph = BatteryService.GlyphFor(pct), GlyphBrush = B("IslandFg"), Title = "On battery",
                    Right = timeLeft == null ? pct + "%" : $"{pct}% · {timeLeft}", RightBrush = B("IslandMute"),
                }, timeLeft == null ? 210 : 300, 40, 2800);
                break;
            default:
                bool critical = ev == BatteryEvent.Critical;
                ShowPopup("pill", "battery", new InfoCard
                {
                    Glyph = BatteryService.GlyphFor(pct), GlyphBrush = B("Bad"),
                    Title = critical ? $"Battery at {pct}% · plug in" : $"Low battery · {pct}%",
                    Button1 = "Saver", Tag1 = "batt-settings",
                }, critical ? 320 : 290, 40, critical ? 9000 : 6000, fullscreenOk: true);
                break;
        }
    }

    public void ShowClipboard(ClipInfo c)
    {
        string title;
        string? sub = null;
        switch (c.Kind)
        {
            case ClipKind.Files:
                title = c.Count == 1 ? "Copied a file" : $"Copied {c.Count} files";
                sub = c.Count == 1 ? c.Text : $"{c.Text} and {c.Count - 1} more";
                break;
            case ClipKind.Image:
                title = "Copied an image";
                break;
            default:
                title = $"Copied · {c.Count} character{(c.Count == 1 ? "" : "s")}";
                if (S.ClipboardShowText)
                {
                    var oneLine = Regex.Replace(c.Text, @"\s+", " ").Trim();
                    sub = oneLine.Length > 90 ? oneLine[..90] + "…" : oneLine;
                }
                break;
        }
        ShowPopup("clip", "clip", new InfoCard { Glyph = "\uE77F", GlyphBrush = B("Accent"), Title = title, Subtitle = sub },
            350, sub == null ? 44 : 58, 2600);
    }

    public void ShowDeadline(string headline, string detail) =>
        ShowPopup("card", "deadline", new InfoCard { Glyph = "\uE787", GlyphBrush = B("Bad"), Title = headline, Subtitle = detail },
            370, 58, 6500);

    public void ShowDownloadDone(string path)
    {
        LastDownloadPath = path;
        ShowPopup("card", "dldone", new InfoCard
        {
            Glyph = "\uE73E", GlyphBrush = B("Good"), Title = "Download complete", Subtitle = System.IO.Path.GetFileName(path),
            Button1 = "Open", Tag1 = "open-file", Button2 = "Folder", Tag2 = "show-file",
        }, 370, 58, 5000);
    }

    public void ShowEarbuds(string name, bool connected, int? battery)
    {
        ShowPopup("card", "earbuds", new InfoCard
        {
            Glyph = "\uE7F6", GlyphBrush = B("IslandFg"), Title = name, Subtitle = connected ? "Connected" : "Disconnected",
            Right = battery is int b ? $"{b}%" : null, RightBrush = battery is < 20 ? B("Bad") : B("Good"),
        }, 320, 58, connected ? 3500 : 2200);
    }

    public void ShowPrivacy(bool camera, string app) =>
        ShowPopup("pill", "privacy", new InfoCard
        {
            Glyph = camera ? "\uE714" : "\uE720", GlyphBrush = B(camera ? "Good" : "Orange"),
            Title = $"{(camera ? "Camera" : "Microphone")} in use · {app}",
        }, 320, 40, 2400);

    public void ShowMessage(string glyph, string brushKey, string text, double width, int ms = 3200, bool fullscreenOk = false) =>
        ShowPopup("pill", "msg:" + text, new InfoCard { Glyph = glyph, GlyphBrush = B(brushKey), Title = text }, width, 40, ms, fullscreenOk);

    /// <summary>The iPhone silent switch: a red bell that wiggles when sound goes off.</summary>
    public void ShowSilent(bool silent) =>
        ShowPopup("silent", "silent:" + silent, new InfoCard
        {
            Glyph = silent ? "\uE7ED" : "\uEA8F",
            AvatarBrush = silent ? B("Bad") : B("Chip"),
            Title = silent ? "Silent" : "Ring",
            RightBrush = silent ? B("Bad") : B("IslandFg"),
        }, 196, 36, 1800, fullscreenOk: true);

    public void ShowTransferDone(string title, string file) =>
        ShowPopup("card", "xferdone", new InfoCard
        {
            Glyph = "\uE73E", GlyphBrush = B("Good"), Title = title, Subtitle = file.Length > 0 ? file : null,
            Button1 = title.StartsWith("Received") ? "Folder" : null, Tag1 = "xfer-folder",
        }, 360, file.Length > 0 ? 58 : 44, 4500);

    // ---------------- Jarvis ----------------

    public void ShowJarvis(JarvisState state) => ShowPopup("jarvis", "jarvis", state, 270, 40, 60000, fullscreenOk: true);

    public void EndJarvis()
    {
        if (_popup != null && (_popup.Kind == "jarvis" || _popup.Kind.StartsWith("jv:"))) EndPopup();
    }

    /// <summary>A Jarvis answer, question or download card. width 0 = fit the text.</summary>
    public void ShowJarvisCard(string glyph, string brushKey, string title, string? sub, string? right, string? button1, string? button2, double width, int ms)
    {
        static string? TagFor(string? label) => label switch
        {
            "Download" => "jv-download", "Yes" => "jv-yes", "No" => "jv-no", null => null, _ => "dismiss",
        };
        if (width <= 0)
        {
            double text = Math.Max(title.Length * 7.3, (sub?.Length ?? 0) * 6.3);
            double buttons = (button1 != null ? 74 : 0) + (button2 != null ? 64 : 0) + (right != null ? 44 : 0);
            width = Math.Clamp(16 + 19 + 12 + text + 10 + buttons + 16, 220, 392);
        }
        ShowPopup("card", "jv:" + title.GetHashCode(), new InfoCard
        {
            Glyph = glyph, GlyphBrush = B(brushKey), Title = title, Subtitle = sub, Right = right, RightBrush = B("IslandMute"),
            Button1 = button1, Tag1 = TagFor(button1), Button2 = button2, Tag2 = TagFor(button2),
        }, width, sub == null ? 44 : 58, ms, fullscreenOk: true);
    }
}
