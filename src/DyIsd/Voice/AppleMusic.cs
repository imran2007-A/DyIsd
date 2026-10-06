using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using DyIsd.Services;

namespace DyIsd.Voice;

/// <summary>
/// "Play Believer on Apple Music": opens Apple Music, types the song into its search box and
/// plays the first song that matches. Apple Music has no command for this, so it's done the way
/// you would by hand, through UI Automation. What it sees is logged ("apple music sees") so it
/// can be tuned if Apple changes the app.
/// </summary>
public static class AppleMusic
{
    const string Proc = "applemusic";

    public static async Task<Reply> PlayAsync(string query, CommandRunner _, bool playlist = false)
    {
        query = query.Trim();
        if (query.Length == 0) return CommandRunner.Bad("Play what?");

        if (!Uia.Running(Proc))
        {
            var app = AppCatalog.Find("apple music");
            if (app == null) return CommandRunner.Bad("Apple Music isn't installed");
            AppCatalog.Launch(app);
            for (int i = 0; i < 50 && Window() == null; i++) await Task.Delay(200);
            await Task.Delay(2000);
        }
        var win = Window();
        if (win == null) return CommandRunner.Bad("Couldn't find Apple Music's window");

        try { CommandRunner.FocusWindow(new IntPtr(win.Current.NativeWindowHandle)); } catch { }
        await Task.Delay(400);

        string result = await Task.Run(() => SearchAndPlay(win, query, playlist));
        return result switch
        {
            "played" => CommandRunner.Ok("\uE768", playlist ? $"Playlist · {query}" : $"Apple Music · {query}"),
            "searched" => CommandRunner.Ok("\uE721", $"Searched Apple Music for \"{query}\". Pick one"),
            _ => CommandRunner.Bad("Couldn't find Apple Music's search box"),
        };
    }

    static AutomationElement? Window() => Uia.WindowsOf(Proc).FirstOrDefault();

    static string SearchAndPlay(AutomationElement win, string query, bool playlist)
    {
        InputSim.WaitForKeysReleased();

        // 1. The search box.
        var edits = Uia.Find(win, ControlType.Edit);
        Log.Write("apple music sees boxes: " + string.Join(" | ", edits.Select(e => e.Name).Take(8)));
        var box = edits.FirstOrDefault(e => e.Name.Contains("search", StringComparison.OrdinalIgnoreCase)) ?? edits.FirstOrDefault();
        if (box == null) return "none";
        try
        {
            box.Element.SetFocus();
            if (box.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                ((ValuePattern)vp).SetValue(query);
            else
            {
                InputSim.Combo(CommandParser.Ctrl, 'A');
                InputSim.Type(query);
            }
            Thread.Sleep(150);
            InputSim.Combo(CommandParser.Enter);
        }
        catch (Exception ex)
        {
            Log.Error("apple music search", ex);
            return "none";
        }

        // Your own playlists live under the Library tab of the results; switch to it if there is one.
        if (playlist)
        {
            Thread.Sleep(900);
            var lib = Uia.Find(win, ControlType.Button, ControlType.TabItem, ControlType.RadioButton, ControlType.ListItem)
                .FirstOrDefault(n => Regex.IsMatch(n.Name, @"^(library|your library|in library)$", RegexOptions.IgnoreCase));
            if (lib != null) AppDriver.Activate(lib.Element);
        }

        // 2. Wait for results, then pick the first song (or playlist) whose name has your words in it.
        var words = AppCatalog.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Thread.Sleep(700);
            var items = Uia.Find(win, ControlType.ListItem, ControlType.DataItem, ControlType.Button, ControlType.Hyperlink, ControlType.Group, ControlType.Text, ControlType.Custom)
                .Where(n => n.Name.Length > 0 && n.Element != box.Element).ToList();
            var matches = items.Where(n =>
            {
                var name = AppCatalog.Normalize(n.Name);
                return words.Count(w => name.Contains(w)) >= Math.Max(1, (words.Length + 1) / 2);
            }).ToList();
            if (matches.Count == 0) continue;

            Log.Write("apple music sees results: " + string.Join(" | ", matches.Take(8).Select(m => $"{m.Type.Replace("ControlType.", "")}:{m.Name}")));
            // Songs first (their names usually mention "Song"), then anything playable.
            var pick = (playlist ? matches.FirstOrDefault(m => m.Name.Contains("playlist", StringComparison.OrdinalIgnoreCase)) : null)
                       ?? matches.FirstOrDefault(m => !playlist && m.Name.Contains("song", StringComparison.OrdinalIgnoreCase))
                       ?? matches.FirstOrDefault(m => m.Type.Contains("ListItem") || m.Type.Contains("DataItem"))
                       ?? matches[0];
            if (Play(pick.Element)) return "played";
            return "searched";
        }
        Log.Write("apple music: no results matched \"" + query + "\"");
        return "searched";
    }

    /// <summary>Plays a result: its own play button if it has one, otherwise a double-click on it.</summary>
    static bool Play(AutomationElement item)
    {
        try
        {
            var playButton = Uia.Find(item, ControlType.Button).FirstOrDefault(b => b.Name.StartsWith("Play", StringComparison.OrdinalIgnoreCase));
            if (playButton != null && Uia.Press(playButton.Element)) return true;
            if (item.TryGetClickablePoint(out var pt))
            {
                InputSim.Click((int)pt.X, (int)pt.Y, twice: true);
                return true;
            }
            var r = item.Current.BoundingRectangle;
            if (!r.IsEmpty)
            {
                InputSim.Click((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2), twice: true);
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("apple music play", ex);
        }
        return false;
    }
}
