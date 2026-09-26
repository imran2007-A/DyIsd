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
///  - Activities that last: music while it plays, focus timer, a download.
///    Only the newest one shows, and never while you're already in the app it belongs to.
///  - Pop-ups that come and go: volume, clipboard, battery, earbuds, deadlines...
///    They take over for a few seconds, then the island goes back to the activity or hides.
/// </summary>
public sealed class IslandController
{
    public event Action<string>? ActionRequested;
    /// <summary>Alt+click on an activity: process names of the app to bring to the front.</summary>
    public event Action<string[]>? JumpRequested;
    public event Action<int, bool>? WheelRequested;
    public event Action<string>? PositionDropped;

    readonly IslandWindow _win;
    readonly MediaState _media;
    readonly FocusTimer _timer;
    readonly DownloadState _download;
    readonly ForegroundWatcher _fg;

    readonly DispatcherTimer _popupTimer = new();
    readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    readonly LevelInfo _volume = new(), _brightness = new();

    sealed record Popup(string Template, string Kind, object Data, double W, double H, bool FullscreenOk, string[]? JumpTo);
    Popup? _popup;
    string? _shownKind;
    bool _expanded, _hovering, _fullscreen, _paused;

    public string? LastDownloadPath { get; private set; }
    public (Brush? Brush, string? App) Privacy { get; set; }

    static AppSettings S => SettingsStore.Current;

    public IslandController(IslandWindow win, MediaState media, FocusTimer timer, DownloadState download, ForegroundWatcher fg)
    {
        _win = win;
        _media = media;
        _timer = timer;
        _download = download;
        _fg = fg;

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
            if (_shownKind == null) return;
            _expanded = !_expanded;
            Render();
        };
        _win.ActionClicked += tag =>
        {
            if (tag == "jump") { Jump(); return; }
            if (tag.StartsWith("jump:")) { EndPopup(); JumpRequested?.Invoke(tag[5..].Split(',')); return; }
            if (tag is "dismiss" or "open-file" or "show-file" or "batt-settings") EndPopup();
            ActionRequested?.Invoke(tag);
        };
        _win.Wheel += (dir, shift) => WheelRequested?.Invoke(dir, shift);
        _win.Dropped += zone => PositionDropped?.Invoke(zone);
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

    /// <summary>The newest running activity whose app you're not currently using.</summary>
    string? PickActivity()
    {
        if (Quiet) return null;
        var list = new List<(string Kind, DateTime Since)>();
        if (S.Features.Media && _media.IsActive && !_fg.IsAny(_media.Processes)) list.Add(("media", _media.ActiveSince));
        if (S.Features.FocusTimer && _timer.IsActive) list.Add(("timer", _timer.StartedAt));
        if (S.Features.Downloads && _download.IsActive && !_fg.IsAny(AppJumper.Browsers)) list.Add(("download", _download.ActiveSince));
        return list.OrderByDescending(a => a.Since).Select(a => a.Kind).FirstOrDefault();
    }

    public void Render()
    {
        if (_paused)
        {
            _win.HideIsland();
            _win.SetPrivacyDot(null, null);
            _shownKind = null;
            return;
        }

        var kind = PickActivity();
        if (kind != _shownKind) _expanded = false;
        _shownKind = kind;
        _win.Expanded = _expanded;

        double? width = null;
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
        }
        else
        {
            _win.HideIsland();
        }

        bool dot = S.Features.Privacy && Privacy.Brush != null && !Quiet;
        _win.SetPrivacyDot(dot ? Privacy.Brush : null, width);
    }

    (string, object, double, double) Spec(string kind, bool expanded) => kind switch
    {
        "media" => expanded ? ("media-e", _media, 366, 166) : ("media-c", _media, 190, 34),
        "timer" => expanded ? ("timer-e", _timer, 330, 128) : ("timer-c", _timer, 150, 34),
        _ => expanded ? ("dl-e", _download, 346, 94) : ("dl-c", _download, 150, 34),
    };

    // ---------------- mouse ----------------

    void OnHover(bool inside)
    {
        _hovering = inside;
        if (_popup != null)
        {
            // Holding Alt over a pop-up keeps it on screen.
            _popupTimer.Stop();
            if (!inside)
            {
                _popupTimer.Interval = TimeSpan.FromMilliseconds(1400);
                _popupTimer.Start();
            }
            else if (!Native.Win32.AltDown)
            {
                _popupTimer.Interval = TimeSpan.FromMilliseconds(2500);
                _popupTimer.Start();
            }
            return;
        }
        if (!inside && _expanded) _collapseTimer.Start();
        else _collapseTimer.Stop();
    }

    void OnClick()
    {
        if (_popup != null)
        {
            if (_popup.JumpTo != null) { var to = _popup.JumpTo; EndPopup(); JumpRequested?.Invoke(to); }
            return;
        }
        Jump();
    }

    /// <summary>Alt+click: go to the app the activity belongs to.</summary>
    void Jump()
    {
        string[]? to = _shownKind switch
        {
            "media" => _media.Processes,
            "download" => AppJumper.Browsers,
            _ => null,
        };
        if (to == null)
        {
            // The timer has no app: a click expands it instead.
            _expanded = !_expanded;
            Render();
            return;
        }
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
}
