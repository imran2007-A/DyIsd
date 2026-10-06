using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using DyIsd.Services;

namespace DyIsd.Voice;

/// <summary>
/// "Play Believer on Apple Music" / "play my gym playlist": opens Apple Music, types into its
/// search box, and starts the best match. Apple Music has no command for this, so it's done the
/// way you would by hand, through UI Automation. It only says "playing" once Windows reports the
/// song actually playing. What it sees is logged ("apple music sees") for tuning.
/// </summary>
public static class AppleMusic
{
    const string Proc = "applemusic";

    public static async Task<Reply> PlayAsync(string query, CommandRunner _, bool playlist = false)
    {
        query = Regex.Replace(query.Trim(), @"\s+(?:from|in|on) (?:my )?(?:library|songs|music|collection)$", "", RegexOptions.IgnoreCase);
        if (query.Length == 0) return CommandRunner.Bad("Play what?");

        if (!Uia.Running(Proc))
        {
            var app = AppCatalog.Find("apple music");
            if (app == null) return CommandRunner.Bad("Apple Music isn't installed");
            AppCatalog.Launch(app);
            for (int i = 0; i < 50 && Window() == null; i++) await Task.Delay(200);
            await Task.Delay(2500); // let it load its library
        }
        var win = Window();
        if (win == null) return CommandRunner.Bad("Couldn't find Apple Music's window");

        try { CommandRunner.FocusWindow(new IntPtr(win.Current.NativeWindowHandle)); } catch { }
        await Task.Delay(500);

        var media = ((App)Application.Current).Media.State;
        string result = await Task.Run(() => SearchAndPlay(win, query, playlist, media));
        string what = playlist ? $"Playlist · {query}" : $"Apple Music · {query}";
        return result switch
        {
            "played" => CommandRunner.Ok("\uE768", what),
            "found" => CommandRunner.Bad($"Found “{query}” in Apple Music but couldn't start it. Click it"),
            "searched" => CommandRunner.Bad($"Searched Apple Music for “{query}”, no clear match. Pick one"),
            _ => CommandRunner.Bad("Couldn't find Apple Music's search box"),
        };
    }

    static AutomationElement? Window() =>
        Uia.WindowsOf(Proc).OrderByDescending(w => { try { var r = w.Current.BoundingRectangle; return r.IsEmpty ? 0 : r.Width * r.Height; } catch { return 0; } }).FirstOrDefault();

    static readonly Regex SearchName = new("search", RegexOptions.IgnoreCase);

    static string SearchAndPlay(AutomationElement win, string query, bool playlist, Island.MediaState media)
    {
        InputSim.WaitForKeysReleased();

        // 1. The search box. Right after Apple Music opens it may not be drawn yet, and in a
        //    narrow window it hides behind a search button: wait, then try the button.
        var box = AppDriver.WaitFor(win, SearchName, 3000, ControlType.Edit)
                  ?? AppDriver.WaitFor(win, new Regex(".*"), 500, ControlType.Edit);
        if (box == null)
        {
            var button = AppDriver.All(win, new Regex("^search$", RegexOptions.IgnoreCase), ControlType.Button, ControlType.ListItem, ControlType.TabItem).FirstOrDefault();
            if (button != null)
            {
                AppDriver.Activate(button.Element);
                box = AppDriver.WaitFor(win, new Regex(".*"), 2000, ControlType.Edit);
            }
        }
        if (box == null)
        {
            Log.Write("apple music sees (no search box): " + AppDriver.Describe(win, 25, ControlType.Button, ControlType.Edit, ControlType.ListItem, ControlType.Text));
            return "none";
        }
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

        // Your own playlists live under the Library tab of the results.
        if (playlist)
        {
            Thread.Sleep(900);
            var lib = Uia.Find(win, ControlType.Button, ControlType.TabItem, ControlType.RadioButton, ControlType.ListItem)
                .FirstOrDefault(n => Regex.IsMatch(n.Name, @"^(library|your library|in library)$", RegexOptions.IgnoreCase));
            if (lib != null) AppDriver.Activate(lib.Element);
        }

        // 2. Wait for results; pick the first song (or playlist) whose name has your words in it.
        var words = AppCatalog.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Thread.Sleep(700);
            var items = Uia.Find(win, ControlType.ListItem, ControlType.DataItem, ControlType.Button, ControlType.Hyperlink, ControlType.Group, ControlType.Text, ControlType.Custom)
                .Where(n => n.Name.Length > 0 && n.Element != box.Element && !n.Name.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
            var matches = items.Where(n =>
            {
                var name = AppCatalog.Normalize(n.Name);
                return words.Count(w => name.Contains(w)) >= Math.Max(1, (words.Length + 1) / 2);
            }).ToList();
            if (matches.Count == 0) continue;

            Log.Write("apple music sees results: " + string.Join(" | ", matches.Take(8).Select(m => $"{m.Type.Replace("ControlType.", "")}:{m.Name}")));
            var kind = playlist ? "playlist" : "song";
            var picks = matches.Where(m => m.Name.Contains(kind, StringComparison.OrdinalIgnoreCase))
                .Concat(matches.Where(m => m.Type.Contains("ListItem") || m.Type.Contains("DataItem")))
                .Concat(matches).Distinct().Take(3).ToList();
            return Play(picks, media) ? "played" : "found";
        }
        Log.Write("apple music: no results matched \"" + query + "\"");
        return "searched";
    }

    /// <summary>
    /// Tries the ways a result can be started, one by one, until Windows reports something new
    /// playing: its own play button, "invoke", a double-click, select + Enter.
    /// </summary>
    static bool Play(List<Uia.Node> picks, Island.MediaState media)
    {
        string before = media.Title + "|" + media.IsPlaying;
        bool Started()
        {
            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(250);
                if (media.IsPlaying && media.Title + "|" + media.IsPlaying != before) return true;
            }
            return false;
        }

        foreach (var pick in picks)
        {
            var ways = new List<(string Name, Func<bool> Try)>
            {
                ("play button", () =>
                {
                    var b = Uia.Find(pick.Element, ControlType.Button).FirstOrDefault(x => x.Name.StartsWith("Play", StringComparison.OrdinalIgnoreCase));
                    return b != null && Uia.Press(b.Element);
                }),
                ("invoke", () => pick.Element.TryGetCurrentPattern(InvokePattern.Pattern, out var p) && Do(() => ((InvokePattern)p).Invoke())),
                ("double-click", () => AppDriver.Click(pick.Element, twice: true)),
                ("select + enter", () =>
                {
                    if (!pick.Element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p)) return false;
                    ((SelectionItemPattern)p).Select();
                    InputSim.Combo(CommandParser.Enter);
                    return true;
                }),
            };
            foreach (var (name, attempt) in ways)
            {
                bool tried;
                try { tried = attempt(); } catch { tried = false; }
                if (!tried) continue;
                if (Started())
                {
                    Log.Write($"apple music played \"{pick.Name}\" via {name}");
                    return true;
                }
            }
        }
        Log.Write("apple music: found results but none started playing");
        return false;
    }

    static bool Do(Action a) { a(); return true; }
}
