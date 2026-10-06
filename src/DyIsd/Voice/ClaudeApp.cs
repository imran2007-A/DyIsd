using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using DyIsd.Services;

namespace DyIsd.Voice;

/// <summary>
/// "Ask Claude to explain recursion" / "new Claude chat": opens the Claude desktop app, starts a
/// new chat if asked, and types your prompt into its message box. It only presses Enter when you
/// say "and send", so you can read or edit it first. What it sees is logged ("claude sees").
/// </summary>
public static class ClaudeApp
{
    static readonly string[] Procs = { "claude" };
    const RegexOptions I = RegexOptions.IgnoreCase;

    public static async Task<Reply> AskAsync(string prompt, bool newChat, bool send)
    {
        string result = await Task.Run(() => Drive(prompt, newChat, send));
        return result switch
        {
            "sent" => CommandRunner.Ok("\uE8BD", "Sent to Claude"),
            "typed" => CommandRunner.Ok("\uE765", "Typed into Claude · say “send” or press Enter"),
            "new" => CommandRunner.Ok("\uE710", "New Claude chat"),
            "noapp" => CommandRunner.Bad("The Claude app isn't installed"),
            _ => CommandRunner.Bad("Couldn't find Claude's message box"),
        };
    }

    static string Drive(string prompt, bool newChat, bool send)
    {
        if (!Uia.Running(Procs))
        {
            var app = AppCatalog.Find("claude");
            if (app == null) return "noapp";
            AppCatalog.Launch(app);
            Thread.Sleep(2500);
        }
        var win = AppDriver.WaitWindow(Procs, 10000);
        if (win == null) return "fail";
        AppDriver.Focus(win);
        InputSim.WaitForKeysReleased();

        if (newChat)
        {
            var btn = AppDriver.WaitFor(win, new Regex(@"^(new chat|start new chat)$", I), 2000, ControlType.Button, ControlType.Hyperlink);
            if (btn != null) { AppDriver.Activate(btn.Element); Thread.Sleep(900); }
            else Log.Write("claude sees buttons: " + AppDriver.Describe(win, 20, ControlType.Button, ControlType.Hyperlink));
            if (prompt.Length == 0) return btn != null ? "new" : "fail";
        }

        var box = AppDriver.WaitFor(win, new Regex(@"(prompt|message|reply|claude|help you|write|talk)", I), 3000, ControlType.Edit, ControlType.Document);
        if (box != null) AppDriver.TypeInto(box.Element, prompt);
        else
        {
            Log.Write("claude sees boxes: " + AppDriver.Describe(win, 12, ControlType.Edit, ControlType.Document, ControlType.Group));
            // The message box always sits at the bottom middle of the window: click there and type.
            var r = win.Current.BoundingRectangle;
            if (r.IsEmpty) return "fail";
            InputSim.Click((int)(r.Left + r.Width / 2), (int)(r.Bottom - 110));
            Thread.Sleep(200);
            InputSim.Type(prompt);
        }
        if (!send) return "typed";
        Thread.Sleep(150);
        InputSim.Combo(CommandParser.Enter);
        return "sent";
    }
}
