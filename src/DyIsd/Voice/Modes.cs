using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DyIsd.Settings;

namespace DyIsd.Voice;

/// <summary>
/// Modes: a name ("Claude mode") plus steps (open Claude, play my playlist, volume 30…).
/// Say "Claude mode" / "start Claude mode" to run it, "end Claude mode" to close what it opened.
/// </summary>
public static class Modes
{
    public sealed record Kind(string Id, string Label, string Hint, string Glyph);

    /// <summary>The step types you can pick in the Control Center, in the order they're shown.</summary>
    public static readonly Kind[] Kinds =
    {
        new("open", "Open app / site", "Claude, VS Code, leetcode.com…", "\uE8A7"),
        new("playlist", "Apple Music playlist", "Playlist name, e.g. Chill Vibes", "\uE93C"),
        new("song", "Apple Music song", "Song name, e.g. Believer", "\uE8D6"),
        new("youtube", "Play on YouTube", "What to play, e.g. lofi hip hop", "\uE768"),
        new("volume", "Volume", "0 – 100", "\uE767"),
        new("brightness", "Brightness", "0 – 100", "\uE706"),
        new("focus", "Focus timer", "Minutes, e.g. 50", "\uE916"),
        new("theme", "Dark / light", "dark or light", "\uE708"),
        new("wifi", "Wi-Fi", "on or off", "\uE701"),
        new("bluetooth", "Bluetooth", "on or off", "\uE702"),
        new("wait", "Wait", "Seconds, e.g. 3", "\uE823"),
        new("type", "Type text", "Exactly what to type", "\uE765"),
        new("close", "Close app", "App name, e.g. Discord", "\uE711"),
        new("say", "Any Jarvis command", "Anything you'd say, e.g. ask claude to plan my day", "\uE720"),
    };

    public static Kind KindOf(string id) => Kinds.FirstOrDefault(k => k.Id == id) ?? Kinds[^1];

    /// <summary>"Play playlist “Chill Vibes”" for the step list.</summary>
    public static string Describe(ModeStep s) => s.Kind switch
    {
        "open" => $"Open {s.Value}",
        "playlist" => $"Play playlist “{s.Value}”",
        "song" => $"Play “{s.Value}” on Apple Music",
        "youtube" => $"Play “{s.Value}” on YouTube",
        "volume" => $"Volume {s.Value}",
        "brightness" => $"Brightness {s.Value}",
        "focus" => $"Focus {s.Value} min",
        "theme" => $"{Cap(s.Value)} mode",
        "wifi" => $"Wi-Fi {s.Value}",
        "bluetooth" => $"Bluetooth {s.Value}",
        "wait" => $"Wait {s.Value} s",
        "type" => $"Type “{s.Value}”",
        "close" => $"Close {s.Value}",
        _ => $"“{s.Value}”",
    };

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Checks a step before saving it. Null = fine, otherwise what's wrong.</summary>
    public static string? Validate(string kind, string value)
    {
        value = value.Trim();
        if (value.Length == 0) return "Fill in the box first";
        switch (kind)
        {
            case "volume" or "brightness":
                return CommandParser.Number(value) is >= 0 and <= 100 ? null : "Use a number from 0 to 100";
            case "focus":
                return CommandParser.Number(value) is >= 1 and <= 600 ? null : "Use minutes, 1 to 600";
            case "wait":
                return CommandParser.Number(value) is >= 1 and <= 120 ? null : "Use seconds, 1 to 120";
            case "theme":
                return value.ToLowerInvariant() is "dark" or "light" ? null : "Type dark or light";
            case "wifi" or "bluetooth":
                return value.ToLowerInvariant() is "on" or "off" ? null : "Type on or off";
        }
        return null;
    }

    /// <summary>What to run for one step.</summary>
    public static List<Cmd> ToCommands(ModeStep s)
    {
        var v = s.Value.Trim();
        int n = CommandParser.Number(v) ?? 0;
        return s.Kind switch
        {
            "open" => new() { new Cmd("open", v) },
            "close" => new() { new Cmd("close-app", v) },
            "playlist" => new() { new Cmd("applemusic-playlist", v) },
            "song" => new() { new Cmd("applemusic", v) },
            "youtube" => new() { new Cmd("yt-play", v, Say: "YouTube · " + v) },
            "volume" => new() { new Cmd("volume-set", N: Math.Clamp(n, 0, 100)) },
            "brightness" => new() { new Cmd("brightness-set", N: Math.Clamp(n, 0, 100)) },
            "focus" => new() { new Cmd("timer", "Focus", Math.Max(1, n) * 60) },
            "theme" => new() { new Cmd("theme", v.ToLowerInvariant()) },
            "wifi" => new() { new Cmd("radio", "wifi", v.Equals("on", StringComparison.OrdinalIgnoreCase) ? 1 : 0) },
            "bluetooth" => new() { new Cmd("radio", "bluetooth", v.Equals("on", StringComparison.OrdinalIgnoreCase) ? 1 : 0) },
            "wait" => new() { new Cmd("wait", N: Math.Clamp(n, 1, 120)) },
            "type" => new() { new Cmd("type", v, Say: "Typed") },
            _ => CommandParser.Parse(v, DateTime.Now),
        };
    }

    static readonly Regex StartWords = new(@"^(?:start|begin|enter|activate|switch to|turn on|go to|go into|lets do|time for)\s+", RegexOptions.IgnoreCase);
    static readonly Regex EndWords = new(@"^(?:end|stop|exit|leave|turn off|finish|deactivate|get out of)\s+", RegexOptions.IgnoreCase);
    // "close claude" closes the app; only "close claude mode" ends the mode.
    static readonly Regex CloseMode = new(@"^(?:close|quit)\s+(?=.+\s(?:mode|session)$)", RegexOptions.IgnoreCase);

    static string Core(string s)
    {
        s = CommandParser.Norm(s);
        s = Regex.Replace(s, @"^(?:the|my)\s+", "");
        s = Regex.Replace(s, @"\s+(?:mode|session|setup|time)$", "");
        return s.Trim();
    }

    /// <summary>The mode you named, and whether you asked to end it. Null when what you said isn't a mode.</summary>
    public static (JarvisMode Mode, bool End)? Match(string text, IEnumerable<JarvisMode> modes)
    {
        var said = CommandParser.Norm(text);
        bool end = EndWords.IsMatch(said) || CloseMode.IsMatch(said);
        var rest = Core(StartWords.Replace(CloseMode.Replace(EndWords.Replace(said, ""), ""), ""));
        if (rest.Length == 0) return null;
        foreach (var m in modes)
        {
            var name = Core(m.Name);
            if (name.Length == 0 || m.Steps.Count == 0) continue;
            if (rest == name || Fuzzy.Score(rest, name) >= 0.86) return (m, end);
        }
        return null;
    }
}
