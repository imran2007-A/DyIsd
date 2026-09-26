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
    public string PlayGlyph => _playing ? "" : "";
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public string PositionText { get => _position; set => Set(ref _position, value); }
    public string DurationText { get => _duration; set => Set(ref _duration, value); }
    public bool HasTimeline { get => _hasTimeline; set => Set(ref _hasTimeline, value); }
    public bool IsActive { get => _active; set => Set(ref _active, value); }
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
