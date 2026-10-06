using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using DyIsd.Services;
using DyIsd.Settings;

namespace DyIsd.Voice;

/// <summary>
/// "Message Rahul saying I'm late" / "call Amma on WhatsApp".
/// People saved in the Control Center (name + number) are opened with WhatsApp's own link
/// (whatsapp://send), which works in every WhatsApp version. Anyone else is searched for in the
/// app, which only works where WhatsApp lets Windows see its search box (the newer app doesn't).
/// Either way it asks you on the island ("Send to Rahul?") before sending or calling anything.
/// What it sees is logged ("whatsapp sees") for tuning.
/// </summary>
public static class WhatsApp
{
    static readonly string[] Procs = { "whatsapp.root", "whatsapp" };
    const RegexOptions I = RegexOptions.IgnoreCase;

    sealed record Found(string Name, AutomationElement Window, AutomationElement Item);

    /// <summary>"98765 43210" → "919876543210"; null if it isn't a phone number.</summary>
    public static string? NormalizeNumber(string text)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00")) digits = digits[2..];
        if (digits.Length == 11 && digits[0] == '0') digits = digits[1..]; // 098765… (Indian trunk 0)
        if (digits.Length == 10) digits = "91" + digits;
        return digits.Length is >= 11 and <= 15 ? digits : null;
    }

    /// <summary>The saved contact closest to what you said, if any is close enough.</summary>
    static WhatsAppContact? Saved(string who)
    {
        var best = SettingsStore.Current.WhatsAppContacts
            .Select(c => (Contact: c, Score: Math.Max(AppDriver.NameScore(who, c.Name),
                c.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => AppDriver.NameScore(who, w) - 0.05).DefaultIfEmpty(0).Max())))
            .Where(x => x.Score >= 0.7)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Contact.Name.Length)
            .FirstOrDefault();
        return best.Contact;
    }

    static string AddHint(string who) =>
        $"Couldn't find \"{who}\" in WhatsApp. Save their number in Control Center \u2192 WhatsApp contacts";

    public static async Task<Reply> MessageAsync(string who, string message)
    {
        string shown = message.Length > 40 ? message[..38] + "\u2026" : message;
        var saved = Saved(who);
        if (saved != null)
        {
            Log.Write($"whatsapp: \"{who}\" is saved contact {saved.Name}");
            return new Reply("\uE8BD", "Good", $"Send to {saved.Name}? \u201C{shown}\u201D", Confirm: async () =>
            {
                bool ok = await Task.Run(() => SendByLink(saved.Number, message));
                return ok ? CommandRunner.Good("\uE8BD", $"Sent to {saved.Name}") : CommandRunner.Bad("WhatsApp didn't open. Nothing was sent");
            });
        }

        var found = await Task.Run(() => FindContact(who));
        if (found == null) return CommandRunner.Bad(AddHint(who));
        return new Reply("\uE8BD", "Good", $"Send to {found.Name}? “{shown}”", Confirm: async () =>
        {
            bool ok = await Task.Run(() => Send(found, message));
            return ok ? CommandRunner.Good("\uE8BD", $"Sent to {found.Name}") : CommandRunner.Bad("Couldn't find WhatsApp's message box. Nothing was sent");
        });
    }

    public static async Task<Reply> CallAsync(string who, bool video)
    {
        var saved = Saved(who);
        if (saved != null)
        {
            return new Reply("\uE717", "Good", $"{(video ? "Video call" : "Call")} {saved.Name}?", Confirm: async () =>
            {
                var r = await Task.Run(() => CallByLink(saved.Number, video));
                return r switch
                {
                    true => CommandRunner.Good("\uE717", $"Calling {saved.Name}"),
                    false => CommandRunner.Good("\uE717", $"Opened {saved.Name}'s chat. Tap the call button"),
                    null => CommandRunner.Bad("WhatsApp didn't open"),
                };
            });
        }

        var found = await Task.Run(() => FindContact(who));
        if (found == null) return CommandRunner.Bad(AddHint(who));
        return new Reply("\uE717", "Good", $"{(video ? "Video call" : "Call")} {found.Name}?", Confirm: async () =>
        {
            bool ok = await Task.Run(() => Call(found, video));
            return ok ? CommandRunner.Good("\uE717", $"Calling {found.Name}") : CommandRunner.Bad("Couldn't find WhatsApp's call button");
        });
    }

    /// <summary>
    /// Opens the chat through WhatsApp's own link with the message already typed in, then presses
    /// Enter. The link always opens the right person's chat, so nothing can land in another chat.
    /// </summary>
    static bool SendByLink(string number, string message)
    {
        var win = OpenLink($"whatsapp://send?phone={number}&text={Uri.EscapeDataString(message)}");
        if (win == null) return false;
        InputSim.WaitForKeysReleased();
        AppDriver.Focus(win);
        Thread.Sleep(300);
        InputSim.Combo(CommandParser.Enter);
        Log.Write($"whatsapp: sent a message to +{number} via link");
        return true;
    }

    /// <summary>true = pressed call, false = chat opened but no call button seen, null = WhatsApp didn't open.</summary>
    static bool? CallByLink(string number, bool video)
    {
        var win = OpenLink($"whatsapp://send?phone={number}");
        if (win == null) return null;
        var btn = AppDriver.WaitFor(win, new Regex(video ? @"^video call" : @"^(voice call|audio call|call)\b", I), 2500, ControlType.Button);
        if (btn == null)
        {
            Log.Write("whatsapp sees buttons in chat: " + AppDriver.Describe(win, 15, ControlType.Button));
            return false;
        }
        return AppDriver.Activate(btn.Element);
    }

    /// <summary>Opens a whatsapp:// link and waits for the chat to load. Returns WhatsApp's window.</summary>
    static AutomationElement? OpenLink(string uri)
    {
        bool cold = !Uia.Running(Procs);
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            Log.Error("whatsapp link", ex);
            return null;
        }
        var win = AppDriver.WaitWindow(Procs, 12000);
        if (win == null) return null;
        Thread.Sleep(cold ? 6000 : 2500); // the chat (and the typed-in text) needs a moment to appear
        AppDriver.Focus(win);
        return win;
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
