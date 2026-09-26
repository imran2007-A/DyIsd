using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DyIsd.Island;

/// <summary>Base class that tells the UI when a property changes, so bindings refresh.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>What the media view shows.</summary>
public sealed class MediaState : Observable
{
    string _title = "", _artist = "", _source = "", _position = "", _duration = "";
    Brush? _art;
    bool _playing, _hasTimeline, _active;
    double _progress;

    public string Title { get => _title; set => Set(ref _title, value); }
    public string Artist { get => _artist; set => Set(ref _artist, value); }
    public string Source { get => _source; set => Set(ref _source, value); }
    public Brush? ArtBrush { get => _art; set => Set(ref _art, value); }
    public bool IsPlaying { get => _playing; set { if (Set(ref _playing, value)) Raise(nameof(PlayGlyph)); } }
    public string PlayGlyph => _playing ? "\uE769" : "\uE768";
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public string PositionText { get => _position; set => Set(ref _position, value); }
    public string DurationText { get => _duration; set => Set(ref _duration, value); }
    public bool HasTimeline { get => _hasTimeline; set => Set(ref _hasTimeline, value); }
    public bool IsActive { get => _active; set => Set(ref _active, value); }
    public string[] Processes { get; set; } = System.Array.Empty<string>();
    public System.DateTime ActiveSince { get; set; }
    Brush _bar = Brushes.White;
    public Brush BarBrush { get => _bar; set => Set(ref _bar, value); }
    public string RemainingText { get => _remaining; set => Set(ref _remaining, value); }
    string _remaining = "";

    bool _hasSession, _shuffle, _canShuffle, _canRepeat, _canSeek;
    int _repeat;
    double _volume;
    /// <summary>A player exists and has a song loaded (playing or paused).</summary>
    public bool HasSession { get => _hasSession; set => Set(ref _hasSession, value); }
    public bool Shuffle { get => _shuffle; set { if (Set(ref _shuffle, value)) Raise(nameof(ShuffleBrush)); } }
    /// <summary>0 = off, 1 = repeat this song, 2 = repeat all.</summary>
    public int Repeat { get => _repeat; set { if (Set(ref _repeat, value)) { Raise(nameof(RepeatBrush)); Raise(nameof(RepeatGlyph)); } } }
    public bool CanShuffle { get => _canShuffle; set => Set(ref _canShuffle, value); }
    public bool CanRepeat { get => _canRepeat; set => Set(ref _canRepeat, value); }
    public bool CanSeek { get => _canSeek; set => Set(ref _canSeek, value); }
    public double Volume { get => _volume; set => Set(ref _volume, value); }
    static readonly Brush Off = MakeBrush(0x80);
    static Brush MakeBrush(byte a) { var b = new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xFF, 0xFF)); b.Freeze(); return b; }
    public Brush ShuffleBrush => _shuffle ? Brushes.White : Off;
    public Brush RepeatBrush => _repeat != 0 ? Brushes.White : Off;
    public string RepeatGlyph => _repeat == 1 ? "\uE8ED" : "\uE8EE";
}

/// <summary>What the download view shows.</summary>
public sealed class DownloadState : Observable
{
    string _file = "", _size = "", _short = "";
    bool _active;

    public string FileName { get => _file; set => Set(ref _file, value); }
    public string SizeText { get => _size; set => Set(ref _size, value); }
    public string ShortText { get => _short; set => Set(ref _short, value); }
    public bool IsActive { get => _active; set => Set(ref _active, value); }
    public System.DateTime ActiveSince { get; set; }
}

/// <summary>A WhatsApp / Discord / Teams call in progress.</summary>
public sealed class CallState : Observable
{
    string _app = "", _who = "", _elapsed = "0:00";
    bool _muted, _active;
    double _level;

    public string AppName { get => _app; set { if (Set(ref _app, value)) Raise(nameof(Subtitle)); } }
    /// <summary>Contact or Discord server name, or the app name when unknown.</summary>
    public string Who { get => _who; set => Set(ref _who, value); }
    public string Elapsed { get => _elapsed; set { if (Set(ref _elapsed, value)) Raise(nameof(Subtitle)); } }
    public string Subtitle => $"{_app} · {_elapsed}";
    public bool Muted { get => _muted; set { if (Set(ref _muted, value)) { Raise(nameof(MuteGlyph)); Raise(nameof(MuteBack)); Raise(nameof(MuteFore)); } } }
    public string MuteGlyph => _muted ? "\uEC54" : "\uE720";
    public Brush MuteBack => _muted ? Brushes.White : MakeBrush(0x30);
    public Brush MuteFore => _muted ? Brushes.Black : Brushes.White;
    /// <summary>Your voice level, 0..1.</summary>
    public double Level { get => _level; set => Set(ref _level, value); }
    public bool IsActive { get => _active; set => Set(ref _active, value); }
    public System.DateTime Since { get; set; }

    static Brush MakeBrush(byte a) { var b = new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xFF, 0xFF)); b.Freeze(); return b; }
}

/// <summary>Volume or brightness pop-up.</summary>
public sealed class LevelInfo : Observable
{
    string _glyph = "", _text = "";
    double _value;

    public string Glyph { get => _glyph; set => Set(ref _glyph, value); }
    public double Value { get => _value; set => Set(ref _value, value); }
    public string Text { get => _text; set => Set(ref _text, value); }
}

/// <summary>One-off pop-up: notification, clipboard, battery, deadline, download done.</summary>
public sealed class InfoCard
{
    public string Glyph { get; init; } = "";
    public Brush? GlyphBrush { get; init; }
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public FontFamily? SubFont { get; init; }
    public string? Right { get; init; }
    public Brush? RightBrush { get; init; }
    public string? Button1 { get; init; }
    public string? Tag1 { get; init; }
    public string? Button2 { get; init; }
    public string? Tag2 { get; init; }
    public ImageSource? Image { get; init; }
    public string? Initial { get; init; }
    public Brush? AvatarBrush { get; init; }
    public string? AppName { get; init; }
}
