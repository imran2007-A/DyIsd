using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DyIsd.Island;
using Windows.Media.Control;
using Windows.Storage.Streams;
using GSMManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GSMSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace DyIsd.Services;

/// <summary>
/// Reads "now playing" from Windows. Every app that shows up in the Windows media flyout
/// (Spotify, Chrome/YouTube, Edge, Media Player, VLC...) is visible here.
/// </summary>
public sealed class MediaService
{
    public MediaState State { get; } = new();
    public event Action? ActiveChanged;

    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    GSMManager? _manager;
    GSMSession? _session;
    TimeSpan _tlPosition, _tlLength;
    DateTimeOffset _tlUpdated;

    public async Task StartAsync()
    {
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
        try
        {
            _manager = await GSMManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _ui.BeginInvoke(() => Attach(SafeCurrent()));
            Attach(SafeCurrent());
        }
        catch (Exception ex)
        {
            Log.Error("media start", ex);
        }
    }

    GSMSession? SafeCurrent()
    {
        try { return _manager?.GetCurrentSession(); }
        catch { return null; }
    }

    void Attach(GSMSession? session)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnProps;
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnTimeline;
        }

        _session = session;
        if (_session == null)
        {
            State.Title = State.Artist = "";
            State.IsPlaying = false;
            Recompute();
            return;
        }

        _session.MediaPropertiesChanged += OnProps;
        _session.PlaybackInfoChanged += OnPlayback;
        _session.TimelinePropertiesChanged += OnTimeline;
        _ = RefreshPropsAsync();
        RefreshPlayback();
        RefreshTimeline();
    }

    // Windows raises these on background threads, so hop back to the UI thread.
    void OnProps(GSMSession s, MediaPropertiesChangedEventArgs e) => _ui.BeginInvoke(() => { _ = RefreshPropsAsync(); });
    void OnPlayback(GSMSession s, PlaybackInfoChangedEventArgs e) => _ui.BeginInvoke(RefreshPlayback);
    void OnTimeline(GSMSession s, TimelinePropertiesChangedEventArgs e) => _ui.BeginInvoke(RefreshTimeline);

    async Task RefreshPropsAsync()
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (s != _session) return;
            State.Title = p.Title ?? "";
            State.Artist = string.IsNullOrWhiteSpace(p.Artist) ? p.AlbumArtist ?? "" : p.Artist;
            State.Source = FriendlyName(s.SourceAppUserModelId);
            State.Processes = ProcessNames(s.SourceAppUserModelId);
            State.BarBrush = State.Source == "Spotify" ? SpotifyGreen : Brushes.White;
            State.ArtBrush = (p.Thumbnail != null ? await LoadArtAsync(p.Thumbnail) : null) ?? FallbackArt(State.Title);
        }
        catch (Exception ex)
        {
            Log.Error("media props", ex);
        }
        Recompute();
    }

    void RefreshPlayback()
    {
        var s = _session;
        if (s == null) return;
        try
        {
            bool playing = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (playing && !State.IsPlaying) State.ActiveSince = DateTime.Now; // resuming counts as new
            State.IsPlaying = playing;
        }
        catch (Exception ex)
        {
            Log.Error("media playback", ex);
        }
        Recompute();
    }

    void RefreshTimeline()
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var t = s.GetTimelineProperties();
            _tlPosition = t.Position;
            _tlLength = t.EndTime - t.StartTime;
            _tlUpdated = t.LastUpdatedTime;
        }
        catch (Exception ex)
        {
            Log.Error("media timeline", ex);
        }
        UpdateTimeline();
    }

    void Tick()
    {
        UpdateTimeline();
        Recompute();
    }

    void UpdateTimeline()
    {
        if (_tlLength <= TimeSpan.Zero)
        {
            State.HasTimeline = false;
            return;
        }

        // Apps only report position now and then, so estimate it between updates.
        var pos = _tlPosition;
        if (State.IsPlaying && _tlUpdated != default) pos += DateTimeOffset.Now - _tlUpdated;
        if (pos > _tlLength) pos = _tlLength;
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;

        State.HasTimeline = true;
        State.Progress = pos.TotalSeconds / _tlLength.TotalSeconds;
        State.PositionText = Format(pos);
        State.DurationText = Format(_tlLength);
        State.RemainingText = "-" + Format(_tlLength - pos);
    }

    void Recompute()
    {
        // Only while something is actually playing: pause or stop and the island hides right away.
        bool active = _session != null && State.Title.Length > 0 && State.IsPlaying;
        if (active == State.IsActive) return;
        State.IsActive = active;
        if (active) State.ActiveSince = DateTime.Now;
        ActiveChanged?.Invoke();
    }

    public async Task TogglePlayPauseAsync() => await Try(s => s.TryTogglePlayPauseAsync().AsTask());
    public async Task NextAsync() => await Try(s => s.TrySkipNextAsync().AsTask());
    public async Task PreviousAsync() => await Try(s => s.TrySkipPreviousAsync().AsTask());

    async Task Try(Func<GSMSession, Task<bool>> action)
    {
        if (_session == null) return;
        try { await action(_session); }
        catch (Exception ex) { Log.Error("media control", ex); }
    }

    static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    static async Task<Brush?> LoadArtAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 128;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }

    static readonly (Color, Color)[] Gradients =
    {
        (Color.FromRgb(0xF7, 0xB7, 0x33), Color.FromRgb(0xFC, 0x4A, 0x1A)),
        (Color.FromRgb(0x43, 0xCE, 0xA2), Color.FromRgb(0x18, 0x5A, 0x9D)),
        (Color.FromRgb(0xEE, 0x09, 0x79), Color.FromRgb(0x2B, 0x10, 0x55)),
        (Color.FromRgb(0x56, 0xAB, 0x2F), Color.FromRgb(0x0B, 0x48, 0x6B)),
    };

    /// <summary>Colorful square for songs with no album art. Same song, same color.</summary>
    static Brush FallbackArt(string title)
    {
        var (a, b) = Gradients[(int)((uint)title.GetHashCode() % Gradients.Length)];
        var g = new LinearGradientBrush(a, b, 45);
        g.Freeze();
        return g;
    }

    static readonly Brush SpotifyGreen = ThemeService.Solid(Color.FromRgb(0x1D, 0xB9, 0x54));

    /// <summary>Process names of the app playing, so we can hide it while you're in that app.</summary>
    static string[] ProcessNames(string id)
    {
        var s = (id ?? "").ToLowerInvariant();
        if (s.Contains("spotify")) return new[] { "spotify" };
        if (s.Contains("msedge")) return new[] { "msedge" };
        if (s.Contains("chrome")) return new[] { "chrome" };
        if (s.Contains("firefox") || s.Contains("308046b0af4a39cb")) return new[] { "firefox" };
        if (s.Contains("brave")) return new[] { "brave" };
        if (s.Contains("opera")) return new[] { "opera" };
        if (s.Contains("zunemusic") || s.Contains("zunevideo")) return new[] { "microsoft.media.player", "music.ui", "video.ui" };
        if (s.Contains("vlc")) return new[] { "vlc" };
        if (s.Contains("applemusic")) return new[] { "applemusic" };
        var name = Path.GetFileNameWithoutExtension((id ?? "").Split('!')[0]).ToLowerInvariant();
        return name.Length > 0 ? new[] { name } : Array.Empty<string>();
    }

    static string FriendlyName(string id)
    {
        var s = (id ?? "").ToLowerInvariant();
        if (s.Contains("spotify")) return "Spotify";
        if (s.Contains("msedge")) return "Edge";
        if (s.Contains("chrome")) return "Chrome";
        if (s.Contains("firefox") || s.Contains("308046b0af4a39cb")) return "Firefox";
        if (s.Contains("brave")) return "Brave";
        if (s.Contains("opera")) return "Opera";
        if (s.Contains("zunemusic") || s.Contains("zunevideo")) return "Media Player";
        if (s.Contains("vlc")) return "VLC";
        if (s.Contains("applemusic")) return "Apple Music";
        var name = id ?? "";
        int bang = name.IndexOf('!');
        if (bang > 0) name = name[..bang];
        name = Path.GetFileNameWithoutExtension(name);
        int us = name.IndexOf('_');
        if (us > 0) name = name[..us];
        int dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1) name = name[(dot + 1)..];
        return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : "Media";
    }
}
