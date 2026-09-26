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
///  - Activities that last (music, focus timer, a download). The first one fills the island,
///    a second one shows as a small bubble. Hover to expand.
///  - Pop-ups that come and go (volume, notification, clipboard...). They take over for a few
///    seconds, then the island goes back to the activity, or hides if there is none.
/// </summary>
public sealed class IslandController
{
    public event Action<string>? ActionRequested;
    public event Action<int, bool>? WheelRequested;
    public event Action<string>? PositionDropped;

    readonly IslandWindow _win;
    readonly MediaState _media;
    readonly FocusTimer _timer;
    readonly DownloadState _download;

    readonly DispatcherTimer _popupTimer = new();
    readonly DispatcherTimer _hoverTimer = new();
    readonly LevelInfo _volume = new(), _brightness = new();

    sealed record Popup(string Template, string Kind, object Data, double W, double H, bool FullscreenOk);
    Popup? _popup;
    string? _primary;
    bool _expanded, _hovering, _pendingExpand, _fullscreen, _paused;

    public string? LastDownloadPath { get; private set; }

    static AppSettings S => SettingsStore.Current;

    public IslandController(IslandWindow win, MediaState media, FocusTimer timer, DownloadState download)
    {
        _win = win;
        _media = media;
        _timer = timer;
        _download = download;

        _popupTimer.Tick += (_, _) => EndPopup();
        _hoverTimer.Tick += (_, _) => OnHoverSettled();

        _win.HoverChanged += OnHover;
        _win.IslandClicked += () =>
        {
            if (_popup == null && _primary != null && !_expanded)
            {
                _expanded = true;
                Render();
            }
        };
        _win.ActionClicked += tag =>
        {
            if (tag is "dismiss" or "open-file" or "show-file" or "batt-settings") EndPopup();
            ActionRequested?.Invoke(tag);
        };
        _win.Wheel += (dir, shift) => WheelRequested?.Invoke(dir, shift);
        _win.Dropped += zone => PositionDropped?.Invoke(zone);
        _win.BubbleClicked += () =>
        {
            var other = Active().FirstOrDefault(k => k != _primary);
            if (other == null) return;
            _primary = other;
            Render();
        };
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

    List<string> Active()
    {
        var list = new List<string>();
        if (Quiet) return list;
        if (S.Features.Media && _media.IsActive) list.Add("media");
        if (S.Features.FocusTimer && _timer.IsActive) list.Add("timer");
        if (S.Features.Downloads && _download.IsActive) list.Add("download");
        return list;
    }

    // ---------------- rendering ----------------

    public void Render()
    {
        if (_paused)
        {
            _win.HideIsland();
            return;
        }

        var list = Active();
        if (_primary == null || !list.Contains(_primary)) _primary = list.FirstOrDefault();
        if (_primary == null) _expanded = false;
        var second = list.FirstOrDefault(k => k != _primary);

        if (_popup != null)
        {
            _win.ShowView(_popup.Template, _popup.Kind, _popup.Data, _popup.W, _popup.H);
            _win.SetBubble(null, null);
            return;
        }

        if (_primary == null)
        {
            _win.HideIsland();
            return;
        }

        var (template, data, w, h) = Spec(_primary, _expanded);
        _win.ShowView(template, template, data, w, h);
        _win.SetBubble(_expanded ? null : second, second == null ? null : DataFor(second));
    }

    (string, object, double, double) Spec(string kind, bool expanded) => kind switch
    {
        "media" => expanded ? ("media-e", _media, 372, 172) : ("media-c", _media, 200, 36),
        "timer" => expanded ? ("timer-e", _timer, 330, 136) : ("timer-c", _timer, 150, 36),
        _ => expanded ? ("dl-e", _download, 360, 96) : ("dl-c", _download, 170, 36),
    };

    object DataFor(string kind) => kind switch { "media" => _media, "timer" => _timer, _ => _download };

    // ---------------- hover ----------------

    void OnHover(bool inside)
    {
        _hovering = inside;
        _hoverTimer.Stop();
        if (_popup != null)
        {
            // Hovering a pop-up keeps it on screen.
            _popupTimer.Stop();
            if (!inside)
            {
                _popupTimer.Interval = TimeSpan.FromMilliseconds(1400);
                _popupTimer.Start();
            }
            return;
        }
        _pendingExpand = inside;
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(inside ? 130 : 260);
        _hoverTimer.Start();
    }

    void OnHoverSettled()
    {
        _hoverTimer.Stop();
        if (_popup != null) return;
        bool want = _pendingExpand && _primary != null;
        if (want == _expanded) return;
        _expanded = want;
        Render();
    }

    // ---------------- pop-ups ----------------

    void ShowPopup(string template, string kind, object data, double w, double h, int ms, bool fullscreenOk = false)
    {
        if (_paused) return;
        if (Quiet && !fullscreenOk) return;
        _popup = new Popup(template, kind, data, w, h, fullscreenOk);
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
        ShowPopup("level", "volume", _volume, 290, 44, 1800, fullscreenOk: true);
    }

    public void ShowBrightness(int level)
    {
        _brightness.Glyph = "";
        _brightness.Value = level / 100.0;
        _brightness.Text = level.ToString();
        ShowPopup("level", "brightness", _brightness, 290, 44, 1800, fullscreenOk: true);
    }

    public void ShowBattery(BatteryEvent ev, int pct, string? timeLeft)
    {
        switch (ev)
        {
            case BatteryEvent.Charging:
                ShowPopup("pill", "battery", new InfoCard
                {
                    Glyph = "", GlyphBrush = B("Good"), Title = "Charging", Right = pct + "%", RightBrush = B("Good"),
                }, 230, 44, 2800);
                break;
            case BatteryEvent.Unplugged:
                ShowPopup("pill", "battery", new InfoCard
                {
                    Glyph = BatteryService.GlyphFor(pct), GlyphBrush = B("IslandFg"), Title = "On battery",
                    Right = timeLeft == null ? pct + "%" : $"{pct}% · {timeLeft}", RightBrush = B("IslandMute"),
                }, timeLeft == null ? 230 : 320, 44, 2800);
                break;
            default:
                bool critical = ev == BatteryEvent.Critical;
                ShowPopup("pill", "battery", new InfoCard
                {
                    Glyph = BatteryService.GlyphFor(pct), GlyphBrush = B("Bad"),
                    Title = critical ? $"Battery at {pct}% · plug in now" : $"Battery low · {pct}%",
                    Button1 = "Battery saver", Tag1 = "batt-settings",
                }, critical ? 380 : 330, 44, critical ? 9000 : 6000, fullscreenOk: true);
                break;
        }
    }

    public void ShowNotification(NotificationInfo n)
    {
        ShowPopup("notif", "notif", new InfoCard
        {
            Title = n.Title,
            Subtitle = n.Body,
            AppName = n.App,
            Image = n.Logo,
            Initial = n.Logo == null && n.App.Length > 0 ? n.App[..1].ToUpperInvariant() : null,
            AvatarBrush = n.Logo == null ? B("Accent") : B("IslandBtn"),
        }, 384, 82, 5000);
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
        ShowPopup("clip", "clip", new InfoCard { Glyph = "", GlyphBrush = B("Accent"), Title = title, Subtitle = sub },
            360, sub == null ? 48 : 62, 2600);
    }

    public void ShowDeadline(string headline, string detail) =>
        ShowPopup("card", "deadline", new InfoCard { Glyph = "", GlyphBrush = B("Accent"), Title = headline, Subtitle = detail },
            380, 62, 6500);

    public void ShowDownloadDone(string path)
    {
        LastDownloadPath = path;
        ShowPopup("card", "dldone", new InfoCard
        {
            Glyph = "", GlyphBrush = B("Good"), Title = "Download complete", Subtitle = System.IO.Path.GetFileName(path),
            Button1 = "Open", Tag1 = "open-file", Button2 = "Folder", Tag2 = "show-file",
        }, 380, 62, 5000);
    }

    public void ShowMessage(string glyph, string brushKey, string text, double width, int ms = 3200, bool fullscreenOk = false) =>
        ShowPopup("pill", "msg:" + text, new InfoCard { Glyph = glyph, GlyphBrush = B(brushKey), Title = text }, width, 44, ms, fullscreenOk);
}
