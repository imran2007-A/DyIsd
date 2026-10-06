using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using DyIsd.Services;

namespace DyIsd.Voice;

/// <summary>
/// "Message Rahul saying I'm late" / "call Amma on WhatsApp".
/// Opens WhatsApp, searches for the person, and asks you on the island with the name WhatsApp
/// actually found ("Send to Rahul Kumar?") before sending or calling anything. Nothing is sent
/// without your yes. What it sees is logged ("whatsapp sees") for tuning.
/// </summary>
public static class WhatsApp
{
    static readonly string[] Procs = { "whatsapp.root", "whatsapp" };
    const RegexOptions I = RegexOptions.IgnoreCase;

    sealed record Found(string Name, AutomationElement Window, AutomationElement Item);

    public static async Task<Reply> MessageAsync(string who, string message)
    {
        var found = await Task.Run(() => FindContact(who));
        if (found == null) return CommandRunner.Bad($"Couldn't find \"{who}\" in WhatsApp");
        string shown = message.Length > 40 ? message[..38] + "…" : message;
        return new Reply("\uE8BD", "Good", $"Send to {found.Name}? “{shown}”", Confirm: async () =>
        {
            bool ok = await Task.Run(() => Send(found, message));
            return ok ? CommandRunner.Good("\uE8BD", $"Sent to {found.Name}") : CommandRunner.Bad("Couldn't find WhatsApp's message box. Nothing was sent");
        });
    }

    public static async Task<Reply> CallAsync(string who, bool video)
    {
        var found = await Task.Run(() => FindContact(who));
        if (found == null) return CommandRunner.Bad($"Couldn't find \"{who}\" in WhatsApp");
        return new Reply("\uE717", "Good", $"{(video ? "Video call" : "Call")} {found.Name}?", Confirm: async () =>
        {
            bool ok = await Task.Run(() => Call(found, video));
            return ok ? CommandRunner.Good("\uE717", $"Calling {found.Name}") : CommandRunner.Bad("Couldn't find WhatsApp's call button");
        });
    }

    /// <summary>Searches WhatsApp's chat list and returns the best match, or null.</summary>
    static Found? FindContact(string who)
    {
        who = who.Trim();
        if (!Uia.Running(Procs))
        {
            var app = AppCatalog.Find("whatsapp");
            if (app == null) return null;
            AppCatalog.Launch(app);
        }
        var win = AppDriver.WaitWindow(Procs, 10000);
        if (win == null) return null;
        AppDriver.Focus(win);
        InputSim.WaitForKeysReleased();

        var box = AppDriver.WaitFor(win, new Regex(@"search", I), 4000, ControlType.Edit);
        if (box == null)
        {
            Log.Write("whatsapp sees boxes: " + AppDriver.Describe(win, 10, ControlType.Edit));
            return null;
        }
        AppDriver.TypeInto(box.Element, who);

        // Results appear as you type; give them a moment, then pick the closest name.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Thread.Sleep(600);
            // Chat rows can be list items, buttons, groups or just text, depending on the WhatsApp version.
            var items = Uia.Find(win, ControlType.ListItem, ControlType.Button, ControlType.DataItem, ControlType.Group, ControlType.Text, ControlType.Custom, ControlType.Hyperlink)
                .Where(n => n.Name.Length > 0 && n.Element != box.Element).ToList();
            var scored = items.Select(n => (Node: n, Name: ContactName(n.Name), Score: AppDriver.NameScore(who, ContactName(n.Name))))
                .Where(x => x.Score >= 0.75)
                .OrderByDescending(x => x.Score).ThenBy(x => x.Name.Length).ToList(); // ties: the contact row, not a message mentioning them
            if (scored.Count == 0) continue;
            Log.Write("whatsapp sees: " + string.Join(" | ", scored.Take(6).Select(x => $"{x.Name} ({x.Score:0.00})")));
            var best = scored[0];
            return new Found(best.Name, win, best.Node.Element);
        }
        Log.Write("whatsapp: nobody matched \"" + who + "\"; window shows: " + AppDriver.Describe(win, 25, ControlType.ListItem, ControlType.Button, ControlType.Text, ControlType.Group, ControlType.Custom));
        return null;
    }

    /// <summary>"Rahul Kumar, Typing…, 10:42 PM" → "Rahul Kumar".</summary>
    static string ContactName(string uiName)
    {
        var first = uiName.Split(new[] { ',', '\n' }, 2)[0].Trim();
        return Regex.Replace(first, @"^(chat with|pinned|unread)\s*:?\s*", "", I);
    }

    static bool OpenChat(Found f)
    {
        AppDriver.Focus(f.Window);
        AppDriver.Activate(f.Item);
        Thread.Sleep(900);
        return true;
    }

    static bool Send(Found f, string message)
    {
        InputSim.WaitForKeysReleased();
        OpenChat(f);
        var box = AppDriver.WaitFor(f.Window, new Regex(@"^(type a message|message)", I), 4000, ControlType.Edit, ControlType.Document);
        if (box == null)
        {
            Log.Write("whatsapp sees boxes in chat: " + AppDriver.Describe(f.Window, 10, ControlType.Edit, ControlType.Document));
            return false;
        }
        AppDriver.TypeInto(box.Element, message);
        Thread.Sleep(150);
        InputSim.Combo(CommandParser.Enter);
        Log.Write($"whatsapp: sent a message to {f.Name}");
        return true;
    }

    static bool Call(Found f, bool video)
    {
        InputSim.WaitForKeysReleased();
        OpenChat(f);
        var btn = AppDriver.WaitFor(f.Window, new Regex(video ? @"^video call" : @"^(voice call|audio call|call)\b", I), 4000, ControlType.Button);
        if (btn == null)
        {
            Log.Write("whatsapp sees buttons in chat: " + AppDriver.Describe(f.Window, 15, ControlType.Button));
            return false;
        }
        return AppDriver.Activate(btn.Element);
    }

    /// <summary>Hangs up a WhatsApp call from its call window.</summary>
    public static bool HangUp()
    {
        foreach (var w in Uia.WindowsOf(Procs))
        {
            var btn = AppDriver.All(w, new Regex(@"^(end call|hang up|leave call)\b", I), ControlType.Button).FirstOrDefault();
            if (btn != null) return AppDriver.Activate(btn.Element);
        }
        return false;
    }
}
